using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

// Imports the DHL "Daily Report" workbook's "Return inbound" sheet — the document DHL sends once
// they've received a tech's returned parts, split into GOOD (back into stock) and BAD (DHL sends
// those for repair itself — no explicit "send to repair" step exists on our side). The very same
// file format gets re-imported weeks/months later once a repaired Serial No. reappears as GOOD,
// closing the repair loop. See MatchRow for the full rule set.
[ApiController]
[Route("[controller]")]
public class DailyReportController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;
    private readonly AuditService _audit;

    public DailyReportController(AppDbContext context, StockService stock, AuditService audit)
    {
        _context = context;
        _stock = stock;
        _audit = audit;
    }

    // POST /DailyReport/preview — parses + matches, but writes nothing. Lets Admin see exactly
    // what an import would do before committing to it.
    [HttpPost("preview")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult Preview(IFormFile file)
    {
        var parsed = ParseFile(file, out var error);
        if (error != null) return BadRequest(new { message = error });

        var (rowResults, summary) = Process(parsed!, commit: false, userName: CurrentUser());

        var minStockRows = ParseMinimumStock(file!);
        List<ReconcileRow>? reconcileRows = null;
        ReconcileSummary? reconcileSummary = null;
        if (minStockRows != null)
            (reconcileRows, reconcileSummary) = ReconcileStock(minStockRows, commit: false, userName: CurrentUser());

        return Ok(new { rows = rowResults, summary, reconcileRows, reconcileSummary });
    }

    // POST /DailyReport/confirm — re-parses the same uploaded file and this time persists every
    // matched row, recording the whole run as one DailyReportImportBatch for history/undo.
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost("confirm")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult Confirm(IFormFile file)
    {
        var parsed = ParseFile(file, out var error);
        if (error != null) return BadRequest(new { message = error });

        var userName = CurrentUser();
        var (rowResults, summary) = Process(parsed!, commit: true, userName: userName);

        var batch = new DailyReportImportBatch
        {
            FileName = file!.FileName,
            ImportedBy = userName,
            TotalRows = parsed!.Count,
            ReturnConfirmedCount = summary.ReturnConfirmed,
            RepairCompletedCount = summary.RepairCompleted,
            StillInRepairCount = summary.StillInRepair,
            UnmatchedCount = summary.Unmatched
        };
        _context.DailyReportImportBatches.Add(batch);
        _context.SaveChanges();

        foreach (var r in rowResults)
        {
            _context.DailyReportImportRows.Add(new DailyReportImportRow
            {
                BatchId = batch.Id,
                RowIndex = r.RowIndex,
                PartNo = r.PartNo,
                PartName = r.PartName,
                SerialNo = r.SerialNo,
                Qty = r.Qty,
                DhlStatus = r.DhlStatus,
                Problem = r.Problem,
                CaseNo = r.CaseNo,
                MatchType = r.MatchType,
                TicketId = r.TicketId,
                WithdrawBatchId = r.WithdrawBatchId,
                PartUnitId = r.PartUnitId,
                StockCredited = r.StockCredited
            });
        }
        _context.SaveChanges();
        _audit.Log(User, "DailyReportImportBatch", batch.Id.ToString(), "IMPORT",
            null, new { batch.FileName, batch.TotalRows, summary });

        var minStockRows = ParseMinimumStock(file!);
        List<ReconcileRow>? reconcileRows = null;
        ReconcileSummary? reconcileSummary = null;
        if (minStockRows != null)
        {
            (reconcileRows, reconcileSummary) = ReconcileStock(minStockRows, commit: true, userName: userName);
            _audit.Log(User, "DailyReportImportBatch", batch.Id.ToString(), "RECONCILE_STOCK",
                null, new { batch.FileName, reconcileSummary });
        }

        return Ok(new { message = "Import เสร็จสิ้น", batch, rows = rowResults, summary, reconcileRows, reconcileSummary });
    }

    // GET /DailyReport/history
    [HttpGet("history")]
    public IActionResult History()
    {
        var batches = _context.DailyReportImportBatches.OrderByDescending(b => b.ImportedAt).ToList();
        return Ok(batches);
    }

    // GET /DailyReport/batches/{id}
    [HttpGet("batches/{id}")]
    public IActionResult BatchDetail(int id)
    {
        var batch = _context.DailyReportImportBatches.FirstOrDefault(b => b.Id == id);
        if (batch == null) return NotFound();
        var rows = _context.DailyReportImportRows.Where(r => r.BatchId == id).OrderBy(r => r.RowIndex).ToList();
        return Ok(new { batch, rows });
    }

    // PUT /DailyReport/rows/{id}/undo — reverses exactly this row's stock/ticket effect.
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPut("rows/{id}/undo")]
    public IActionResult UndoRow(int id)
    {
        var row = _context.DailyReportImportRows.FirstOrDefault(r => r.Id == id);
        if (row == null) return NotFound();
        if (row.Undone) return BadRequest(new { message = "แถวนี้ถูกย้อนกลับไปแล้ว" });

        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "DHL-BKK");
        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");
        var userName = CurrentUser();

        try
        {
            if (row.MatchType == "ReturnConfirmed" && row.WithdrawBatchId.HasValue)
            {
                var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == row.WithdrawBatchId);
                var line = _context.TicketPartLines.FirstOrDefault(l => l.WithdrawBatchId == row.WithdrawBatchId && l.LineType == "Return" && l.PartNo == row.PartNo);
                if (batch == null || line == null)
                    return BadRequest(new { message = "ไม่พบใบเบิก/รายการที่เกี่ยวข้องแล้ว — อาจถูกลบหรือแก้ไขไปหลังจาก import" });

                // Put it back where it was: tech's on-hand bucket, minus whichever central bucket it landed in.
                _stock.AdjustStock(row.PartNo, techLoc?.Id ?? 0, row.Qty, "Good", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถว import #{id}", serialNo: row.SerialNo);
                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, -row.Qty, row.DhlStatus == "GOOD" ? "Good" : "Repair", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถว import #{id}", serialNo: row.SerialNo);

                line.ConfirmedQty = Math.Max(0, line.ConfirmedQty - row.Qty);
                if (batch.ReturnStatus == "คืน") batch.ReturnStatus = "เดินทาง"; // reopen — no longer fully confirmed

                if (row.PartUnitId.HasValue)
                {
                    var unit = _context.PartUnits.FirstOrDefault(u => u.Id == row.PartUnitId);
                    if (unit != null) { _context.PartUnits.Remove(unit); }
                }
            }
            else if (row.MatchType == "RepairCompleted" && row.PartUnitId.HasValue)
            {
                var unit = _context.PartUnits.FirstOrDefault(u => u.Id == row.PartUnitId);
                if (unit == null || unit.Status != "InStock")
                    return BadRequest(new { message = "สถานะอะไหล่ชิ้นนี้เปลี่ยนไปแล้วหลังจาก import — ย้อนกลับไม่ได้อัตโนมัติ" });

                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, -row.Qty, "Good", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถว import #{id}", serialNo: row.SerialNo, partUnitId: unit.Id);
                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, "Repair", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถว import #{id}", serialNo: row.SerialNo, partUnitId: unit.Id);
                unit.Status = "InRepair";
            }
            else if (row.MatchType == "Unmatched" && row.StockCredited)
            {
                // Credited straight to the central warehouse with no Ticket involved (see Process,
                // Rule 3) — reverse only that, checking the PartUnit's status first exactly like
                // RepairCompleted above, so a serial this row created that a LATER import already
                // moved on from (e.g. matched a return, or completed repair) doesn't get silently
                // deleted out from under that later row.
                var expectedStatus = row.DhlStatus == "GOOD" ? "InStock" : "InRepair";
                if (row.PartUnitId.HasValue)
                {
                    var unit = _context.PartUnits.FirstOrDefault(u => u.Id == row.PartUnitId);
                    if (unit == null || unit.Status != expectedStatus)
                        return BadRequest(new { message = "สถานะอะไหล่ชิ้นนี้เปลี่ยนไปแล้วหลังจาก import — ย้อนกลับไม่ได้อัตโนมัติ" });
                    _context.PartUnits.Remove(unit);
                }
                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, -row.Qty, row.DhlStatus == "GOOD" ? "Good" : "Repair",
                    "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถว import #{id}", serialNo: row.SerialNo);
            }
            // StillInRepair rows, and an Unmatched row that never found the Part No. at all
            // (StockCredited == false), never touched anything — nothing to undo.
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        row.Undone = true;
        row.UndoneAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "DailyReportImportRow", id.ToString(), "UNDO", null, new { row.PartNo, row.SerialNo });
        return Ok(new { message = "ย้อนกลับแล้ว", row });
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    public class ParsedRow
    {
        public int RowIndex { get; set; }
        public string PartNo { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public string SerialNo { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string Status { get; set; } = string.Empty; // GOOD | BAD
        public string? Problem { get; set; }
        public string? CaseNo { get; set; } // present on newer exports only — see Process()
    }

    // Finds the "Return inbound" sheet by header text (not hardcoded position) so a reordered or
    // renamed-but-same-shape workbook still parses. Requires at minimum Part Number, Serial
    // Number, Qty and an INVENTORY STATUS/Status column within the first 5 rows of some sheet.
    private List<ParsedRow>? ParseFile(IFormFile? file, out string? error)
    {
        error = null;
        if (file == null || file.Length == 0) { error = "กรุณาแนบไฟล์"; return null; }
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx") { error = "รองรับเฉพาะไฟล์ .xlsx"; return null; }

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);

            IXLWorksheet? ws = null;
            int headerRow = -1, cPartNo = -1, cPartName = -1, cSerial = -1, cQty = -1, cStatus = -1, cProblem = -1, cCaseNo = -1;

            var preferred = wb.Worksheets.Contains("Return inbound")
                ? new[] { wb.Worksheet("Return inbound") }.Concat(wb.Worksheets)
                : wb.Worksheets.AsEnumerable();

            foreach (var candidate in preferred)
            {
                int r1 = -1, p1 = -1, pn1 = -1, s1 = -1, q1 = -1, st1 = -1, pr1 = -1, cn1 = -1;
                var lastRowScan = Math.Min(5, candidate.LastRowUsed()?.RowNumber() ?? 1);
                var lastColScan = candidate.LastColumnUsed()?.ColumnNumber() ?? 1;
                for (int r = 1; r <= lastRowScan; r++)
                    for (int c = 1; c <= lastColScan; c++)
                    {
                        var val = candidate.Cell(r, c).GetString().Trim().Replace("\n", " ");
                        if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { p1 = c; r1 = r; }
                        else if (val.Equals("Part Description", StringComparison.OrdinalIgnoreCase)) pn1 = c;
                        else if (val.Replace("_", "").Equals("SERIALNUMBER", StringComparison.OrdinalIgnoreCase) || val.Equals("Serial Number", StringComparison.OrdinalIgnoreCase)) s1 = c;
                        else if (val.Equals("QTY", StringComparison.OrdinalIgnoreCase)) q1 = c;
                        else if (val.Replace("\n", "").Contains("INVENTORY STATUS", StringComparison.OrdinalIgnoreCase) || val.Equals("Status", StringComparison.OrdinalIgnoreCase)) st1 = c;
                        else if (val.Equals("Problem", StringComparison.OrdinalIgnoreCase)) pr1 = c;
                        // Newer Daily Report exports (Mar 2026+) added this column — when present it
                        // ties a row straight to the Aservice ticket (== our ExternalTicketNo),
                        // letting us match exactly instead of guessing by Part No. Older exports
                        // don't have it at all, so it stays optional (cn1 can remain -1).
                        else if (val.Equals("Case No", StringComparison.OrdinalIgnoreCase)) cn1 = c;
                    }
                if (p1 >= 0 && s1 >= 0 && q1 >= 0 && st1 >= 0)
                {
                    ws = candidate; headerRow = r1; cPartNo = p1; cPartName = pn1; cSerial = s1; cQty = q1; cStatus = st1; cProblem = pr1; cCaseNo = cn1;
                    break;
                }
            }

            if (ws == null)
            {
                error = "ไม่พบชีตที่มีคอลัมน์ Part Number, Serial Number, QTY และ Status — เช็คว่าเป็นไฟล์ Daily Report ที่ถูกต้อง";
                return null;
            }

            var rows = new List<ParsedRow>();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var partNo = ws.Cell(r, cPartNo).GetString().Trim();
                if (string.IsNullOrWhiteSpace(partNo)) continue;

                var status = ws.Cell(r, cStatus).GetString().Trim().ToUpperInvariant();
                if (status != "GOOD" && status != "BAD") continue; // skip section headers/blank separators

                var caseNo = cCaseNo > 0 ? ws.Cell(r, cCaseNo).GetString().Trim() : "";
                rows.Add(new ParsedRow
                {
                    RowIndex = r,
                    PartNo = partNo,
                    PartName = cPartName > 0 ? ws.Cell(r, cPartName).GetString().Trim() : "",
                    SerialNo = ws.Cell(r, cSerial).GetString().Trim(),
                    Qty = (int)(ws.Cell(r, cQty).GetValue<double?>() ?? 1),
                    Status = status,
                    Problem = cProblem > 0 ? ws.Cell(r, cProblem).GetString().Trim() : null,
                    CaseNo = string.IsNullOrWhiteSpace(caseNo) ? null : caseNo
                });
            }
            return rows;
        }
        catch (Exception ex)
        {
            error = $"อ่านไฟล์ไม่สำเร็จ: {ex.Message}";
            return null;
        }
    }

    // ── Stock reconciliation (Minimum Stock sheet) ──────────────────────────────
    // The "Return inbound" sheet is transactional (each row is one event) and, being a running
    // log DHL never trims, self-heals from a skipped day on its own once the next file is
    // imported. But it only ever reflects what DHL told us happened — any drift from a source
    // outside that (a missed report entirely, a manual correction on DHL's side, a miscount)
    // never gets corrected by it. "Minimum Stock" is DHL's own current on-hand count, so treating
    // it as the source of truth and overwriting our DHL-BKK quantities to match — every time,
    // not just once at setup — means the numbers can never drift for more than one day no matter
    // what else went wrong. Runs in the same Preview/Confirm pass as the Return inbound rows,
    // from the same uploaded file, so Admin never has to remember a second step.
    public class ReconcileRow
    {
        public string PartNo { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public string? OldQty { get; set; }
        public string NewQty { get; set; } = string.Empty;
        public string Bucket { get; set; } = string.Empty; // "Good" or "Repair"
        public bool Changed { get; set; }
    }

    public class ReconcileSummary
    {
        public int PartsMatched { get; set; }
        public int PartsChanged { get; set; }
        public int NotFoundCount { get; set; }
        public List<string> NotFoundSample { get; set; } = new();
    }

    // Reads "Minimum Stock" from the same workbook — Part Number / AVAILABLE_QTY / INVENTORY_STS
    // (GOOD/BAD/NO INVENTORY). Optional: an older-format Daily Report simply won't have this
    // sheet, and that's fine — Return inbound processing still runs on its own.
    private List<(string PartNo, int AvailableQty, string Status)>? ParseMinimumStock(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        if (!wb.Worksheets.Contains("Minimum Stock")) return null;

        var ws = wb.Worksheet("Minimum Stock");
        int headerRow = -1, pnCol = -1, qtyCol = -1, stsCol = -1;
        var lastRowScan = Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1);
        var lastColScan = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        for (int r = 1; r <= lastRowScan; r++)
            for (int c = 1; c <= lastColScan; c++)
            {
                var val = ws.Cell(r, c).GetString().Trim();
                if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { pnCol = c; headerRow = r; }
                else if (val.Equals("AVAILABLE_QTY", StringComparison.OrdinalIgnoreCase)) qtyCol = c;
                else if (val.Equals("INVENTORY_STS", StringComparison.OrdinalIgnoreCase)) stsCol = c;
            }
        if (pnCol < 0 || qtyCol < 0 || stsCol < 0) return null;

        var rows = new List<(string, int, string)>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = ws.Cell(r, pnCol).GetString().Trim();
            if (string.IsNullOrWhiteSpace(partNo)) continue;
            var qty = (int)(ws.Cell(r, qtyCol).GetValue<double?>() ?? 0);
            var status = ws.Cell(r, stsCol).GetString().Trim().ToUpperInvariant();
            rows.Add((partNo, qty, status));
        }
        return rows;
    }

    // BAD → the RepairQty bucket (DHL is holding it for repair, same convention Rule 1/3 above
    // use). Everything else (GOOD, NO INVENTORY, anything unrecognized) → GoodQty — NO INVENTORY
    // rows carry AVAILABLE_QTY 0 anyway, so this just zeroes GoodQty for those, which is correct.
    // Only the bucket the row names gets touched; the other condition's quantity is left alone,
    // since one row never speaks to both at once.
    private (List<ReconcileRow> Rows, ReconcileSummary Summary) ReconcileStock(
        List<(string PartNo, int AvailableQty, string Status)> minStockRows, bool commit, string userName)
    {
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "DHL-BKK");
        var partsByNo = _context.Parts.ToDictionary(p => p.PartNo, p => p);
        var results = new List<ReconcileRow>();
        var summary = new ReconcileSummary();
        var notFound = new HashSet<string>();

        foreach (var row in minStockRows)
        {
            if (!partsByNo.TryGetValue(row.PartNo, out var part))
            {
                notFound.Add(row.PartNo);
                continue;
            }
            summary.PartsMatched++;

            var bucket = row.Status == "BAD" ? "Repair" : "Good";
            var stock = _context.PartStocks.FirstOrDefault(s => s.PartId == part.Id && s.LocationId == (mainWh != null ? mainWh.Id : 0));
            var currentQty = stock == null ? 0 : (bucket == "Repair" ? stock.RepairQty : stock.GoodQty);
            var delta = row.AvailableQty - currentQty;
            var changed = delta != 0;
            if (changed) summary.PartsChanged++;

            results.Add(new ReconcileRow
            {
                PartNo = row.PartNo, PartName = part.PartName, Bucket = bucket,
                OldQty = currentQty.ToString(), NewQty = row.AvailableQty.ToString(), Changed = changed
            });

            if (commit && changed)
            {
                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, delta, bucket,
                    "Reconcile", "DailyReportMinimumStock", null, userName,
                    $"ปรับยอดให้ตรงกับ Minimum Stock ของ DHL ({currentQty} → {row.AvailableQty})");
            }
        }

        summary.NotFoundCount = notFound.Count;
        summary.NotFoundSample = notFound.Take(30).ToList();
        if (commit) _context.SaveChanges();
        return (results, summary);
    }

    // ── Matching ─────────────────────────────────────────────────────────────

    public class RowResult
    {
        public int RowIndex { get; set; }
        public string PartNo { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public string SerialNo { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string DhlStatus { get; set; } = string.Empty;
        public string? Problem { get; set; }
        public string? CaseNo { get; set; }
        public string MatchType { get; set; } = string.Empty;
        public int? TicketId { get; set; }
        public int? WithdrawBatchId { get; set; }
        public string? ExternalTicketNo { get; set; }
        public int? PartUnitId { get; set; }
        public string? Note { get; set; }
        // Only meaningful for MatchType == Unmatched — ReturnConfirmed/RepairCompleted always move
        // stock, StillInRepair never does, so only Unmatched needs to say which case it was (part
        // not found in our system vs. credited to the central warehouse anyway).
        public bool StockCredited { get; set; }
    }

    public class Summary
    {
        public int ReturnConfirmed { get; set; }
        public int RepairCompleted { get; set; }
        public int StillInRepair { get; set; }
        public int Unmatched { get; set; }
    }

    // Runs the matching rules over every row, in file order:
    //   1) Serial already PartUnit.Status == InRepair → this row is that repair coming back.
    //      GOOD  → RepairCompleted (RepairQty → GoodQty, PartUnit → InStock).
    //      BAD   → StillInRepair (no-op — DHL is telling us it's still there).
    //   2) Otherwise, look for an open return-leg Ticket line for this PartNo with quantity still
    //      unconfirmed (oldest ticket first). If found → ReturnConfirmed: moves it out of the
    //      tech's on-hand bucket into GoodQty (DHL said GOOD) or RepairQty (DHL said BAD, and a
    //      PartUnit is created so a later reappearance as GOOD is recognized by rule 1). The
    //      ticket only finalizes to คืน once every one of its Return lines is fully confirmed —
    //      a single Daily Report file covering only part of what was returned is expected.
    //   3) No match on either rule → Unmatched, flagged for Admin to check by hand.
    // commit=false runs the exact same logic against live DB reads (so cross-batch state like an
    // already-InRepair Serial is honored) but never calls AdjustStock/SaveChanges — used for the
    // preview. Multiple rows within ONE file can still affect each other correctly because the
    // FIFO "remaining quantity" tracking and the PartUnit-status overlay below are both in-memory
    // for the whole pass regardless of commit.
    private (List<RowResult> Rows, Summary Summary) Process(List<ParsedRow> rows, bool commit, string userName)
    {
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "DHL-BKK");
        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");
        var partsByNo = _context.Parts.ToDictionary(p => p.PartNo, p => p);

        var openReturnLines = _context.TicketPartLines
            .Include(l => l.Ticket)
            .Include(l => l.WithdrawBatch)
            .Where(l => l.LineType == "Return" && l.WithdrawBatch!.ReturnStatus == "เดินทาง" && l.WithdrawBatch.ReturnAddress != null && l.ConfirmedQty < l.Quantity)
            .OrderBy(l => l.WithdrawBatch!.UpdatedAt)
            .ToList();
        var remaining = openReturnLines.ToDictionary(l => l.TicketPartLineId, l => l.Quantity - l.ConfirmedQty);

        // Overlay of PartUnit status changes made earlier in THIS pass, keyed by Serial No. — lets
        // rule 1 see a unit this same file already put InRepair a few rows up, without hitting the DB.
        var serialStatusOverlay = new Dictionary<string, string>();

        var results = new List<RowResult>();
        var summary = new Summary();

        foreach (var row in rows)
        {
            // A blank SerialNo (common for small unserialized parts like connectors) can't be
            // tracked as an individual unit at all — treating "" as a real dictionary key would
            // wrongly link every blank-serial row together as if they were the same physical item.
            var currentUnitStatus = string.IsNullOrWhiteSpace(row.SerialNo)
                ? null
                : serialStatusOverlay.TryGetValue(row.SerialNo, out var overlaid)
                    ? overlaid
                    : _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo)?.Status;

            // Rule 1 — repair completing / still in repair.
            if (currentUnitStatus == "InRepair")
            {
                if (row.Status == "GOOD")
                {
                    summary.RepairCompleted++;
                    serialStatusOverlay[row.SerialNo] = "InStock";
                    int? unitId = null;
                    if (commit)
                    {
                        var unit = _context.PartUnits.First(u => u.SerialNo == row.SerialNo);
                        _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, -row.Qty, "Repair", "RepairComplete", "DailyReportRow", null, userName, $"ซ่อมเสร็จ (Daily Report) SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unit.Id);
                        _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, "Good", "RepairComplete", "DailyReportRow", null, userName, $"ซ่อมเสร็จ (Daily Report) SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unit.Id);
                        unit.Status = "InStock";
                        unit.Condition = "Good";
                        _context.SaveChanges();
                        unitId = unit.Id;
                    }
                    results.Add(new RowResult { RowIndex = row.RowIndex, PartNo = row.PartNo, PartName = row.PartName, SerialNo = row.SerialNo, Qty = row.Qty, DhlStatus = row.Status, Problem = row.Problem, CaseNo = row.CaseNo, MatchType = "RepairCompleted", PartUnitId = unitId, Note = "ซ่อมเสร็จ กลับเข้าสต็อกดี" });
                }
                else
                {
                    summary.StillInRepair++;
                    results.Add(new RowResult { RowIndex = row.RowIndex, PartNo = row.PartNo, PartName = row.PartName, SerialNo = row.SerialNo, Qty = row.Qty, DhlStatus = row.Status, Problem = row.Problem, CaseNo = row.CaseNo, MatchType = "StillInRepair", Note = "ยังซ่อมไม่เสร็จ" });
                }
                continue;
            }

            // Rule 2 — first-time return confirmation against an open return line.
            // Prefer an exact match by Case No. (== Ticket.ExternalTicketNo) when the file gives
            // one — newer Daily Report exports (Mar 2026+) always do, and it removes the FIFO
            // guesswork entirely. If a Case No. IS present but matches no open line, that's
            // Unmatched rather than silently falling back to a Part No. guess (a wrong guess is
            // worse than flagging it for Admin). Only guess by Part No. + FIFO when the row has no
            // Case No. at all — i.e. an older export.
            var matchedByCaseNo = !string.IsNullOrWhiteSpace(row.CaseNo);
            var line = matchedByCaseNo
                ? openReturnLines.FirstOrDefault(l => l.PartNo == row.PartNo && l.Ticket!.ExternalTicketNo == row.CaseNo && remaining.GetValueOrDefault(l.TicketPartLineId) > 0)
                : openReturnLines.FirstOrDefault(l => l.PartNo == row.PartNo && remaining.GetValueOrDefault(l.TicketPartLineId) > 0);
            if (line != null)
            {
                remaining[line.TicketPartLineId] = Math.Max(0, remaining[line.TicketPartLineId] - row.Qty);
                summary.ReturnConfirmed++;
                if (!string.IsNullOrWhiteSpace(row.SerialNo))
                    serialStatusOverlay[row.SerialNo] = row.Status == "GOOD" ? "InStock" : "InRepair";

                int? unitId = null;
                if (commit)
                {
                    _stock.AdjustStock(row.PartNo, techLoc?.Id ?? 0, -row.Qty, "Good", "Return", "WithdrawBatch", line.WithdrawBatchId?.ToString(), userName, $"คืนผ่าน Daily Report SN {row.SerialNo}", serialNo: row.SerialNo);
                    _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, row.Status == "GOOD" ? "Good" : "Repair", "Return", "WithdrawBatch", line.WithdrawBatchId?.ToString(), userName, $"คืนผ่าน Daily Report SN {row.SerialNo}", serialNo: row.SerialNo);

                    if (!string.IsNullOrWhiteSpace(row.SerialNo) && partsByNo.TryGetValue(row.PartNo, out var part))
                    {
                        var unit = _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo);
                        if (unit == null)
                        {
                            unit = new PartUnit { SerialNo = row.SerialNo, PartId = part.Id };
                            _context.PartUnits.Add(unit);
                        }
                        unit.Status = row.Status == "GOOD" ? "InStock" : "InRepair";
                        unit.Condition = row.Status == "GOOD" ? "Good" : "Bad";
                        unit.LocationId = mainWh?.Id;
                        _context.SaveChanges();
                        unitId = unit.Id;
                    }

                    line.ConfirmedQty += row.Qty;
                    // "All confirmed" is scoped to THIS batch's own Return lines now — a Ticket can
                    // have several batches each returning independently, so finishing one must not
                    // touch any other batch's return leg.
                    var allLinesConfirmed = _context.TicketPartLines
                        .Where(l => l.WithdrawBatchId == line.WithdrawBatchId && l.LineType == "Return")
                        .All(l => l.TicketPartLineId == line.TicketPartLineId ? line.ConfirmedQty >= l.Quantity : l.ConfirmedQty >= l.Quantity);
                    if (allLinesConfirmed && line.WithdrawBatch != null)
                    {
                        line.WithdrawBatch.ReturnStatus = "คืน";
                        line.WithdrawBatch.UpdatedAt = DateTime.Now;
                    }
                    _context.SaveChanges();
                }

                var matchNote = row.Status == "GOOD" ? "คืนสำเร็จ — เข้าสต็อกดี" : "คืนสำเร็จ — เข้าสถานะกำลังซ่อม";
                matchNote += matchedByCaseNo ? " (จับคู่ตรงด้วย Case No.)" : " (จับคู่แบบเดาด้วย Part No. — ไฟล์นี้ไม่มี Case No.)";
                results.Add(new RowResult
                {
                    RowIndex = row.RowIndex, PartNo = row.PartNo, PartName = row.PartName, SerialNo = row.SerialNo, Qty = row.Qty,
                    DhlStatus = row.Status, Problem = row.Problem, MatchType = "ReturnConfirmed", CaseNo = row.CaseNo,
                    TicketId = line.TicketId, WithdrawBatchId = line.WithdrawBatchId, ExternalTicketNo = line.Ticket?.ExternalTicketNo, PartUnitId = unitId,
                    Note = matchNote
                });
                continue;
            }

            // Rule 3 — no open return line matches this row (no Ticket submitted a return for it,
            // or it hasn't reached "เดินทาง" yet in our own tracking). DHL has physically received
            // the part regardless of whether our app's return flow ever caught up to it, and Admin
            // needs the warehouse count to be right every single day so techs can withdraw against
            // it — so the stock still goes in, just not tied to any Ticket/WithdrawBatch. Flagged
            // as Unmatched in the report for Admin to reconcile later if a Ticket does turn up.
            summary.Unmatched++;
            var partExists = partsByNo.TryGetValue(row.PartNo, out var unmatchedPart);
            // Don't credit a serial we already have a PartUnit record for — re-importing the same
            // Daily Report file (or a serial this same pass already handled a few rows up) must not
            // add the same physical unit's stock twice. Only a genuinely new serial (or a row with
            // no serial at all, which we can't dedupe) gets credited.
            var alreadyTracked = !string.IsNullOrWhiteSpace(row.SerialNo) && currentUnitStatus != null;
            var shouldCredit = partExists && !alreadyTracked;
            int? unmatchedUnitId = null;
            if (shouldCredit)
            {
                if (!string.IsNullOrWhiteSpace(row.SerialNo))
                    serialStatusOverlay[row.SerialNo] = row.Status == "GOOD" ? "InStock" : "InRepair";
                if (commit)
                {
                    _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, row.Status == "GOOD" ? "Good" : "Repair",
                        "Return", "DailyReportRow", null, userName, $"รับเข้าจาก Daily Report — ไม่พบใบเบิก/คืนที่ตรงกัน SN {row.SerialNo}", serialNo: row.SerialNo);

                    if (!string.IsNullOrWhiteSpace(row.SerialNo))
                    {
                        var unit = new PartUnit { SerialNo = row.SerialNo, PartId = unmatchedPart!.Id };
                        _context.PartUnits.Add(unit);
                        unit.Status = row.Status == "GOOD" ? "InStock" : "InRepair";
                        unit.Condition = row.Status == "GOOD" ? "Good" : "Bad";
                        unit.LocationId = mainWh?.Id;
                        _context.SaveChanges();
                        unmatchedUnitId = unit.Id;
                    }
                }
            }

            var unmatchedNote = matchedByCaseNo
                ? $"มี Case No. ({row.CaseNo}) แต่ไม่พบ Ticket ที่รอคืนอยู่ตรงกัน"
                : "ไม่พบ Ticket ที่รอคืนอยู่สำหรับอะไหล่นี้";
            unmatchedNote += !partExists
                ? " — ไม่พบ Part No. นี้ในระบบ จึงเพิ่มสต็อกให้ไม่ได้"
                : alreadyTracked
                    ? " — SN นี้เคยถูกบันทึกไว้แล้ว ไม่เพิ่มสต็อกซ้ำ"
                    : (row.Status == "GOOD" ? " — เพิ่มเข้าสต็อกดีที่คลังกลางแล้ว (ยังไม่ผูกกับใบเบิก)" : " — เพิ่มเข้าสต็อกซ่อมที่คลังกลางแล้ว (ยังไม่ผูกกับใบเบิก)");
            results.Add(new RowResult
            {
                RowIndex = row.RowIndex, PartNo = row.PartNo, PartName = row.PartName, SerialNo = row.SerialNo, Qty = row.Qty,
                DhlStatus = row.Status, Problem = row.Problem, MatchType = "Unmatched", CaseNo = row.CaseNo, PartUnitId = unmatchedUnitId,
                Note = unmatchedNote, StockCredited = shouldCredit
            });
        }

        return (results, summary);
    }

    private string CurrentUser() =>
        User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
        ?? User?.Identity?.Name
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? "system";
}
