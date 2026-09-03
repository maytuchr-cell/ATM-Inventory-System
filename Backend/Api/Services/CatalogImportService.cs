using ClosedXML.Excel;
using Api.Models;

namespace Api.Services;

// Bulk-import support for the GRG-style spare-parts catalog Excel files (the only master-data
// source for Parts at Production go-live — there's no other seed path once DatabaseProvider is
// MySQL, see the isSqlite-gated seed blocks in Program.cs). One upload does two things in
// sequence against the SAME file bytes: create/update Part rows, then link "Same part no."
// columns into EquivalentGroups (logic moved here from EquivalentGroupController.ImportFromExcel,
// which now just calls LinkEquivalents — kept as its own endpoint too, for re-linking a catalog
// without touching Parts).
public class CatalogImportService
{
    private readonly AppDbContext _context;
    public CatalogImportService(AppDbContext context) => _context = context;

    public record PartRow(
        int RowNumber, string PartNo, string PartName, string? MainUnit, string? Zone,
        string? Remark, string? DeviceType, string? ImagePath, string Status, string? Note);

    public record PartsPreviewResult(int TotalRows, int NewCount, int UpdateCount, int ErrorCount, List<PartRow> Rows);
    public record PartsConfirmResult(int Inserted, int Updated, int Skipped, int ErrorCount);
    public record LinkResult(int RowsProcessed, int GroupsCreated, int GroupsMerged, int MembersAdded, int NotFoundCount, List<string> NotFoundSample);

    private static readonly string[] KnownDeviceTypes = { "ADM", "ATM", "CDM" };

    // Finds the sheet with "Part Number" + "Part Description" headers, scanning the first 10 rows
    // of every sheet (column order isn't consistent across these catalog files — Main Unit/Sub
    // Unit swap order between the GSB and "Sub Unit Edition" exports — so every column is located
    // by header text, never by fixed position).
    private (IXLWorksheet ws, int headerRow, Dictionary<string, int> cols)? FindPartsSheet(XLWorkbook wb)
    {
        foreach (var candidate in wb.Worksheets)
        {
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int headerRow = -1;
            var lastRowScan = Math.Min(10, candidate.LastRowUsed()?.RowNumber() ?? 1);
            var lastColScan = candidate.LastColumnUsed()?.ColumnNumber() ?? 1;
            for (int r = 1; r <= lastRowScan; r++)
            {
                var rowCols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c <= lastColScan; c++)
                {
                    var val = candidate.Cell(r, c).GetString().Trim();
                    if (val.Length == 0) continue;
                    if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) rowCols["PartNumber"] = c;
                    else if (val.Equals("Part Description", StringComparison.OrdinalIgnoreCase)) rowCols["PartDescription"] = c;
                    else if (val.Equals("Main Unit", StringComparison.OrdinalIgnoreCase)) rowCols["MainUnit"] = c;
                    else if (val.Equals("Sub Unit", StringComparison.OrdinalIgnoreCase)) rowCols["SubUnit"] = c;
                    else if (val.Equals("Picture", StringComparison.OrdinalIgnoreCase)) rowCols["Picture"] = c;
                    else if (val.Equals("Remark", StringComparison.OrdinalIgnoreCase)) rowCols["Remark"] = c;
                    else if (val.Equals("Same part no.", StringComparison.OrdinalIgnoreCase)) rowCols["SamePartNo"] = c;
                }
                if (rowCols.ContainsKey("PartNumber") && rowCols.ContainsKey("PartDescription"))
                {
                    headerRow = r;
                    cols = rowCols;
                    break;
                }
            }
            if (headerRow > 0) return (candidate, headerRow, cols);
        }
        return null;
    }

    // "ADM,GHB,BOC,VTM" -> "ADM" (only ADM/ATM/CDM are tracked device types — other codes in the
    // Remark column, like GHB/BOC/VTM, aren't a DeviceType this system models, so they're ignored
    // rather than guessed at). Multiple tracked types on one part -> comma-combo, e.g. "ADM,ATM".
    private static string? ParseDeviceType(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark)) return null;
        var tokens = remark.Split(new[] { ',', '/', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToUpperInvariant()).ToHashSet();
        var found = KnownDeviceTypes.Where(tokens.Contains).ToList();
        return found.Count > 0 ? string.Join(",", found) : null;
    }

    // Only "Upper Unit"/"Lower Unit" exactly, from the "Sub Unit" column — everything else stays
    // null (unknown) rather than guessed, matching the hand-maintained backfill this replaces.
    private static string? ParseZone(string? subUnit)
    {
        if (string.IsNullOrWhiteSpace(subUnit)) return null;
        var v = subUnit.Trim();
        if (v.Equals("Upper Unit", StringComparison.OrdinalIgnoreCase)) return "Upper";
        if (v.Equals("Lower Unit", StringComparison.OrdinalIgnoreCase)) return "Lower";
        return null;
    }

    public PartsPreviewResult PreviewParts(byte[] fileBytes)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var found = FindPartsSheet(wb) ?? throw new InvalidOperationException(
            "ไม่พบชีตที่มีคอลัมน์ 'Part Number' และ 'Part Description' ในไฟล์นี้");
        var (ws, headerRow, cols) = found;

        var existingPartNos = _context.Parts.Select(p => p.PartNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<PartRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;

        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = ws.Cell(r, cols["PartNumber"]).GetString().Trim();
            var partName = ws.Cell(r, cols["PartDescription"]).GetString().Trim();
            if (string.IsNullOrWhiteSpace(partNo) && string.IsNullOrWhiteSpace(partName)) continue; // blank row

            string status; string? note;
            if (string.IsNullOrWhiteSpace(partNo) || string.IsNullOrWhiteSpace(partName))
            {
                status = "Error"; note = "Part Number หรือ Part Description ว่าง";
            }
            else if (!seenInFile.Add(partNo))
            {
                status = "Error"; note = "ซ้ำกันเองในไฟล์นี้";
            }
            else
            {
                status = existingPartNos.Contains(partNo) ? "Update" : "New";
                note = null;
            }

            var mainUnit = cols.TryGetValue("MainUnit", out var mc) ? ws.Cell(r, mc).GetString().Trim() : null;
            var subUnit = cols.TryGetValue("SubUnit", out var sc) ? ws.Cell(r, sc).GetString().Trim() : null;
            var remark = cols.TryGetValue("Remark", out var rc) ? ws.Cell(r, rc).GetString().Trim() : null;
            var picture = cols.TryGetValue("Picture", out var pc) ? ws.Cell(r, pc).GetString().Trim() : null;

            rows.Add(new PartRow(
                RowNumber: r,
                PartNo: partNo,
                PartName: partName,
                MainUnit: string.IsNullOrWhiteSpace(mainUnit) ? null : mainUnit,
                Zone: ParseZone(subUnit),
                Remark: string.IsNullOrWhiteSpace(remark) ? null : remark,
                DeviceType: ParseDeviceType(remark),
                ImagePath: string.IsNullOrWhiteSpace(picture) ? null : picture,
                Status: status,
                Note: note));
        }

        return new PartsPreviewResult(
            TotalRows: rows.Count,
            NewCount: rows.Count(x => x.Status == "New"),
            UpdateCount: rows.Count(x => x.Status == "Update"),
            ErrorCount: rows.Count(x => x.Status == "Error"),
            Rows: rows);
    }

    public PartsConfirmResult ConfirmParts(byte[] fileBytes, string? project, bool overwriteExisting, string addedBy)
    {
        var preview = PreviewParts(fileBytes);
        int inserted = 0, updated = 0, skipped = 0;

        var partsByNo = _context.Parts.ToDictionary(p => p.PartNo, StringComparer.OrdinalIgnoreCase);

        foreach (var row in preview.Rows)
        {
            if (row.Status == "Error") continue;

            if (row.Status == "New")
            {
                var part = new Part
                {
                    PartNo = row.PartNo,
                    PartName = row.PartName,
                    MainUnit = row.MainUnit,
                    Zone = row.Zone,
                    Remark = row.Remark,
                    DeviceType = row.DeviceType,
                    ImagePath = row.ImagePath,
                    Project = string.IsNullOrWhiteSpace(project) ? null : project.Trim(),
                    AddedBy = addedBy,
                    AddedDate = DateTime.Now,
                };
                _context.Parts.Add(part);
                partsByNo[row.PartNo] = part;
                inserted++;
            }
            else // "Update"
            {
                if (!overwriteExisting) { skipped++; continue; }
                var part = partsByNo[row.PartNo];
                part.PartName = row.PartName;
                if (row.MainUnit != null) part.MainUnit = row.MainUnit;
                if (row.Zone != null) part.Zone = row.Zone;
                if (row.Remark != null) part.Remark = row.Remark;
                if (row.DeviceType != null) part.DeviceType = row.DeviceType;
                if (row.ImagePath != null) part.ImagePath = row.ImagePath;
                if (!string.IsNullOrWhiteSpace(project)) part.Project = project.Trim();
                part.UpdatedAt = DateTime.UtcNow;
                updated++;
            }
        }
        _context.SaveChanges();

        return new PartsConfirmResult(inserted, updated, skipped, preview.ErrorCount);
    }

    // Moved from EquivalentGroupController.ImportFromExcel — same union-find over the "Part
    // Number"/"Same part no." columns, unchanged. Called right after ConfirmParts (on the same
    // file bytes) so newly-created parts are already in the DB and can be linked in one upload;
    // also still reachable standalone via POST /EquivalentGroup/import for re-linking a catalog
    // without touching Parts.
    public LinkResult LinkEquivalents(byte[] fileBytes, string sourceLabel)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));

        IXLWorksheet? ws = null;
        int headerRow = -1, partNoCol = -1, sameCol = -1;
        foreach (var candidate in wb.Worksheets)
        {
            int r1 = -1, c1 = -1, c2 = -1;
            var lastRowScan = Math.Min(10, candidate.LastRowUsed()?.RowNumber() ?? 1);
            var lastColScan = candidate.LastColumnUsed()?.ColumnNumber() ?? 1;
            for (int r = 1; r <= lastRowScan && (c1 < 0 || c2 < 0); r++)
                for (int c = 1; c <= lastColScan; c++)
                {
                    var val = candidate.Cell(r, c).GetString().Trim();
                    if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { c1 = c; r1 = r; }
                    if (val.Equals("Same part no.", StringComparison.OrdinalIgnoreCase)) c2 = c;
                }
            if (c1 >= 0 && c2 >= 0) { ws = candidate; headerRow = r1; partNoCol = c1; sameCol = c2; break; }
        }
        if (ws == null)
            return new LinkResult(0, 0, 0, 0, 0, new List<string>());

        static string Norm(string s) => s.Trim().TrimStart('-').Trim();

        var dbPartNos = _context.Parts.Select(p => p.PartNo).ToHashSet();
        var parent = new Dictionary<string, string>();
        string Find(string x)
        {
            if (!parent.ContainsKey(x)) parent[x] = x;
            return parent[x] == x ? x : (parent[x] = Find(parent[x]));
        }
        void Union(string a, string b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        var notFound = new HashSet<string>();
        int rowsProcessed = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;

        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = Norm(ws.Cell(r, partNoCol).GetString());
            if (string.IsNullOrWhiteSpace(partNo)) continue;
            rowsProcessed++;

            if (!dbPartNos.Contains(partNo)) { notFound.Add(partNo); continue; }
            Find(partNo);

            var sameRaw = ws.Cell(r, sameCol).GetString();
            if (string.IsNullOrWhiteSpace(sameRaw)) continue;

            foreach (var raw in sameRaw.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = Norm(raw);
                if (string.IsNullOrWhiteSpace(eq)) continue;
                if (!dbPartNos.Contains(eq)) { notFound.Add(eq); continue; }
                Union(partNo, eq);
            }
        }

        var components = parent.Keys.GroupBy(Find).Where(g => g.Count() > 1).ToList();

        var groupIdByPartNo = _context.EquivalentGroupMembers
            .ToList()
            .GroupBy(m => m.PartNo)
            .ToDictionary(g => g.Key, g => g.First().GroupId);

        int groupsCreated = 0, groupsMerged = 0, membersAdded = 0;

        foreach (var comp in components)
        {
            var partNos = comp.ToList();
            var existingGroupIds = partNos.Where(groupIdByPartNo.ContainsKey).Select(p => groupIdByPartNo[p]).Distinct().ToList();

            int targetGroupId;
            if (existingGroupIds.Count == 0)
            {
                var group = new EquivalentGroup { Name = $"Imported: {partNos[0]}", Description = $"Imported from {sourceLabel}" };
                _context.EquivalentGroups.Add(group);
                _context.SaveChanges();
                targetGroupId = group.Id;
                groupsCreated++;
            }
            else
            {
                targetGroupId = existingGroupIds[0];
                groupsMerged++;
            }

            foreach (var pn in partNos)
            {
                if (_context.EquivalentGroupMembers.Any(m => m.GroupId == targetGroupId && m.PartNo == pn)) continue;
                var part = _context.Parts.First(p => p.PartNo == pn);
                _context.EquivalentGroupMembers.Add(new EquivalentGroupMember { GroupId = targetGroupId, PartId = part.Id, PartNo = pn });
                groupIdByPartNo[pn] = targetGroupId;
                membersAdded++;
            }
            _context.SaveChanges();
        }

        return new LinkResult(rowsProcessed, groupsCreated, groupsMerged, membersAdded, notFound.Count, notFound.Take(30).ToList());
    }
}
