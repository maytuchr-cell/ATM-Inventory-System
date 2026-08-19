using Microsoft.AspNetCore.Mvc;
using Api.Models;
using ClosedXML.Excel;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class EquivalentGroupController : ControllerBase
{
    private readonly AppDbContext _context;
    public EquivalentGroupController(AppDbContext context) => _context = context;

    // GET /api/EquivalentGroup
    [HttpGet]
    public IActionResult GetAll()
    {
        var groups = _context.EquivalentGroups.ToList();
        var members = _context.EquivalentGroupMembers.ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var result = groups.Select(g => new
        {
            g.Id, g.Name, g.Description, g.CreatedAt,
            members = members
                .Where(m => m.GroupId == g.Id)
                .Select(m => new { m.Id, m.PartNo, partName = partMap.GetValueOrDefault(m.PartNo, m.PartNo) })
                .ToList()
        });

        return Ok(result);
    }

    // POST /api/EquivalentGroup
    [HttpPost]
    public IActionResult Create([FromBody] GroupWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Group name is required." });

        var group = new EquivalentGroup { Name = dto.Name.Trim(), Description = dto.Description?.Trim() };
        _context.EquivalentGroups.Add(group);
        _context.SaveChanges();
        return Ok(new { message = "Group created.", group });
    }

    // PUT /api/EquivalentGroup/{id}
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] GroupWriteDto dto)
    {
        var group = _context.EquivalentGroups.FirstOrDefault(g => g.Id == id);
        if (group == null) return NotFound();

        group.Name = dto.Name?.Trim() ?? group.Name;
        group.Description = dto.Description?.Trim();
        _context.SaveChanges();
        return Ok(new { message = "Updated.", group });
    }

    // DELETE /api/EquivalentGroup/{id}
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var group = _context.EquivalentGroups.FirstOrDefault(g => g.Id == id);
        if (group == null) return NotFound();
        _context.EquivalentGroups.Remove(group);
        _context.SaveChanges();
        return Ok(new { message = "Deleted." });
    }

    // GET /api/EquivalentGroup/for-part/{partNo} — other parts in the same equivalent group(s),
    // for Admin to substitute in when the requested part is out of stock at Ticket-approval time.
    [HttpGet("for-part/{partNo}")]
    public IActionResult GetEquivalentsForPart(string partNo)
    {
        var groupIds = _context.EquivalentGroupMembers.Where(m => m.PartNo == partNo).Select(m => m.GroupId).ToHashSet();
        if (!groupIds.Any()) return Ok(new List<object>());

        var equivalentPartNos = _context.EquivalentGroupMembers
            .Where(m => groupIds.Contains(m.GroupId) && m.PartNo != partNo)
            .Select(m => m.PartNo)
            .Distinct()
            .ToList();

        var stockByPartId = _context.PartStocks
            .GroupBy(s => s.PartId)
            .Select(g => new { PartId = g.Key, Qty = g.Sum(x => x.GoodQty) })
            .ToDictionary(x => x.PartId, x => x.Qty);

        var result = _context.Parts
            .Where(p => equivalentPartNos.Contains(p.PartNo) && p.IsActive)
            .ToList()
            .Select(p => new { p.PartNo, p.PartName, stockQuantity = stockByPartId.GetValueOrDefault(p.Id, 0) });

        return Ok(result);
    }

    // POST /api/EquivalentGroup/import — bulk-create/merge equivalent groups from a GRG-style
    // catalog Excel file. Looks for a "Part Number" column and a "Same part no." column,
    // searched across the first 10 rows of EVERY sheet (not just the first) — an admin adding
    // an instructions/cover sheet ahead of the data is a reasonable thing to do, so the first
    // sheet that actually has both headers wins, not necessarily sheet 1.
    // "Same part no." may list several PartNos, one per line within the cell. Cells that
    // Excel stored as numbers (leading-zero-free) are converted to text before matching.
    // Real GRG catalog files carry embedded part photos and can run 30-50MB, past Kestrel's
    // 30MB default request-body cap — raise it for this one upload endpoint.
    [HttpPost("import")]
    [RequestSizeLimit(200_000_000)]
    public IActionResult ImportFromExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported." });

        using var stream = file.OpenReadStream();
        try
        {
            using var wb = new XLWorkbook(stream);

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
                return BadRequest(new { message = "Could not find a sheet with 'Part Number' and 'Same part no.' columns in its first 10 rows." });

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

                // Source data isn't consistent about the separator — some cells use one PartNo
                // per line, others comma- or semicolon-separate several on one line.
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
                    var group = new EquivalentGroup { Name = $"Imported: {partNos[0]}", Description = $"Imported from {file.FileName}" };
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

            return Ok(new
            {
                message = "Import complete.",
                rowsProcessed,
                groupsCreated,
                groupsMerged,
                membersAdded,
                notFoundCount = notFound.Count,
                notFoundSample = notFound.Take(30).ToList()
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Could not read the file: {ex.Message}" });
        }
    }

    // POST /api/EquivalentGroup/{id}/members
    [HttpPost("{id}/members")]
    public IActionResult AddMember(int id, [FromBody] MemberDto dto)
    {
        if (!_context.EquivalentGroups.Any(g => g.Id == id))
            return NotFound(new { message = "Group not found." });

        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.PartNo);
        if (part == null)
            return BadRequest(new { message = $"Part {dto.PartNo} not found." });

        if (_context.EquivalentGroupMembers.Any(m => m.GroupId == id && m.PartNo == dto.PartNo))
            return BadRequest(new { message = $"{dto.PartNo} is already in this group." });

        var member = new EquivalentGroupMember { GroupId = id, PartId = part.Id, PartNo = dto.PartNo };
        _context.EquivalentGroupMembers.Add(member);
        _context.SaveChanges();
        return Ok(new { message = "Member added.", member });
    }

    // DELETE /api/EquivalentGroup/{id}/members/{memberId}
    [HttpDelete("{id}/members/{memberId}")]
    public IActionResult RemoveMember(int id, int memberId)
    {
        var member = _context.EquivalentGroupMembers.FirstOrDefault(m => m.Id == memberId && m.GroupId == id);
        if (member == null) return NotFound();
        _context.EquivalentGroupMembers.Remove(member);
        _context.SaveChanges();
        return Ok(new { message = "Member removed." });
    }
}

public class GroupWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MemberDto
{
    public string PartNo { get; set; } = string.Empty;
}
