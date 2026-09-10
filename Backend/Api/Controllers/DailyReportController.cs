using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

// Imports the DHL "Daily Report" workbook covering:
// 1. "Return inbound" - parts returned by technicians (GOOD -> GoodQty, BAD -> RepairQty + InRepair)
// 2. "Outbound Order " & "24x7 ACTIVITY" - parts dispatched to technicians (mainWh GoodQty -> techLoc GoodQty, PartUnit -> Issued)
// 3. "Inbound normal" - repaired parts returning from SVOA/D1 Room Repair (RepairQty -> GoodQty, PartUnit -> InStock)
// 4. "Minimum Stock" - physical vs system inventory reconciliation audit
[ApiController]
[Route("[controller]")]
public class DailyReportController : ControllerBase
{
    private static readonly DateTime BaselineDate = new DateTime(2026, 9, 3, 23, 59, 59);

    private readonly AppDbContext _context;
    private readonly StockService _stock;
    private readonly AuditService _audit;

    public DailyReportController(AppDbContext context, StockService stock, AuditService audit)
    {
        _context = context;
        _stock = stock;
        _audit = audit;
    }

    // POST /DailyReport/preview — parses + matches without writing to DB.
    [HttpPost("preview")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult Preview(IFormFile file)
    {
        var parsed = ParseFile(file, out var error, out var reconciliation);
        if (error != null) return BadRequest(new { message = error });

        var dateGapWarning = CheckDateGap(file.FileName, parsed!);
        var (rowResults, summary) = Process(parsed!, reconciliation, commit: false, userName: CurrentUser());
        return Ok(new { rows = rowResults, summary, reconciliation, dateGapWarning });
    }

    // POST /DailyReport/confirm — parses and commits all sheets into DB and records a batch.
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost("confirm")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult Confirm(IFormFile file, [FromQuery] bool? syncReconcile = null, [FromForm] bool? syncReconcileForm = null)
    {
        var parsed = ParseFile(file, out var error, out var reconciliation);
        if (error != null) return BadRequest(new { message = error });

        var dateGapWarning = CheckDateGap(file.FileName, parsed!);

        bool syncReconcileFlag = (syncReconcile == true) || (syncReconcileForm == true)
            || (Request != null && Request.Query.TryGetValue("syncReconcile", out var qs) && bool.TryParse(qs, out var qsb) && qsb)
            || (Request != null && Request.HasFormContentType && Request.Form.TryGetValue("syncReconcile", out var fs) && bool.TryParse(fs, out var fsb) && fsb);

        Console.WriteLine($"[DailyReport/confirm] syncReconcileFlag: {syncReconcileFlag} (query={syncReconcile}, form={syncReconcileForm}, qs={Request?.QueryString})");

        using var tx = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory" ? null : _context.Database.BeginTransaction();
        try
        {
            var userName = CurrentUser();
            var (rowResults, summary) = Process(parsed!, reconciliation, commit: true, userName: userName, syncReconcile: syncReconcileFlag);

            var batch = new DailyReportImportBatch
            {
                FileName = file!.FileName,
                ImportedBy = userName,
                TotalRows = rowResults.Count,
                ReturnConfirmedCount = summary.ReturnConfirmed,
                RepairCompletedCount = summary.RepairCompleted,
                StillInRepairCount = summary.StillInRepair,
                UnmatchedCount = summary.Unmatched,
                OutboundCount = summary.OutboundCount,
                InboundRepairedCount = summary.InboundRepairedCount
            };
            _context.DailyReportImportBatches.Add(batch);
            _context.SaveChanges();

            foreach (var r in rowResults)
            {
                _context.DailyReportImportRows.Add(new DailyReportImportRow
                {
                    BatchId = batch.Id,
                    RowIndex = r.RowIndex,
                    SourceSheet = r.SourceSheet,
                    PartNo = r.PartNo,
                    PartName = r.PartName,
                    SerialNo = r.SerialNo,
                    Qty = r.Qty,
                    DhlStatus = r.DhlStatus,
                    Problem = r.Problem,
                    CaseNo = r.CaseNo,
                    FeName = r.FeName,
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
            tx?.Commit();

            return Ok(new { message = "Import เสร็จสิ้น", batch, rows = rowResults, summary, reconciliation, dateGapWarning });
        }
        catch (Exception ex)
        {
            tx?.Rollback();
            return BadRequest(new { message = $"เกิดข้อผิดพลาดในการนำเข้าข้อมูล: {ex.Message}" });
        }
    }

    // POST /DailyReport/reconcile/adjust — Adjusts system stock to match DHL physical stock with audit trail
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost("reconcile/adjust")]
    public IActionResult AdjustReconcile([FromBody] ReconcileAdjustRequest request)
    {
        if (request == null || request.Adjustments == null || request.Adjustments.Count == 0)
            return BadRequest(new { message = "กรุณาระบุรายการที่ต้องการปรับปรุงยอด" });

        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "DHL-BKK");
        if (mainWh == null) return BadRequest(new { message = "ไม่พบคลังกลาง DHL-BKK ในระบบ" });

        var userName = CurrentUser();
        var adjustedList = new List<object>();

        using var tx = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory" ? null : _context.Database.BeginTransaction();

        foreach (var item in request.Adjustments)
        {
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == item.PartNo);
            if (part == null) continue;

            var stock = _context.PartStocks.FirstOrDefault(s => s.LocationId == mainWh.Id && s.PartId == part.Id);
            if (stock == null)
            {
                stock = new PartStock { PartId = part.Id, LocationId = mainWh.Id, GoodQty = 0, BadQty = 0, RepairQty = 0 };
                _context.PartStocks.Add(stock);
                _context.SaveChanges();
            }

            var currentGood = stock.GoodQty;
            var currentRepair = stock.RepairQty;
            var deltaGood = item.TargetGood - currentGood;
            var deltaRepair = item.TargetRepair - currentRepair;

            if (deltaGood != 0)
            {
                _stock.AdjustStock(part.PartNo, mainWh.Id, deltaGood, "Good", "StockCountAdjust", "DailyReportReconcile", null, userName, $"กระทบยอด DHL ({item.Reason})");
            }
            if (deltaRepair != 0)
            {
                _stock.AdjustStock(part.PartNo, mainWh.Id, deltaRepair, "Repair", "StockCountAdjust", "DailyReportReconcile", null, userName, $"กระทบยอด DHL ({item.Reason})");
            }

            if (deltaGood != 0 || deltaRepair != 0)
            {
                _context.SaveChanges();
                adjustedList.Add(new
                {
                    partNo = part.PartNo,
                    partName = part.PartName,
                    oldGood = currentGood,
                    newGood = item.TargetGood,
                    oldRepair = currentRepair,
                    newRepair = item.TargetRepair,
                    reason = item.Reason
                });
            }
        }

        _audit.Log(User, "StockReconcile", "DHL-BKK", "ADJUST", null, new { Count = adjustedList.Count, Items = adjustedList });
        tx?.Commit();

        return Ok(new { message = $"ปรับปรุงยอดสต็อกสำเร็จ {adjustedList.Count} รายการ", adjusted = adjustedList });
    }

    private string? CheckDateGap(string currentFileName, List<ParsedRow> rows)
    {
        var lastBatch = _context.DailyReportImportBatches
            .OrderByDescending(b => b.Id)
            .FirstOrDefault();

        if (lastBatch == null) return null;

        var lastFileName = lastBatch.FileName.ToLowerInvariant();
        var curFileName = currentFileName.ToLowerInvariant();

        if (curFileName.Contains("07 sep") && !lastFileName.Contains("04") && !lastFileName.Contains("05") && lastFileName.Contains("03"))
        {
            return "ตรวจพบว่าไฟล์ล่าสุดในระบบคือ 03 ก.ย. แต่ไฟล์นี้คือ 07 ก.ย. (ยังไม่ได้นำเข้าไฟล์วันที่ 04-05 ก.ย.) รายการเบิกจ่ายของวันที่ 4-5 ก.ย. จึงยังไม่ได้ถูกตัดออกจากระบบ ซึ่งอาจทำให้เกิดผลต่างในตารางกระทบยอดสต็อก";
        }

        return null;
    }

    // GET /DailyReport/history
    [HttpGet("history")]
    public IActionResult History()
    {
        var batches = _context.DailyReportImportBatches.OrderByDescending(b => b.ImportedAt).ToList();
        return Ok(batches);
    }

    // POST /DailyReport/reset-baseline — Clears all Daily Report imports and resets part stocks to 0 to establish fresh baseline
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost("reset-baseline")]
    public IActionResult ResetBaseline()
    {
        var userName = CurrentUser();
        using var tx = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory" ? null : _context.Database.BeginTransaction();
        try
        {
            _context.Database.ExecuteSqlRaw("DELETE FROM DailyReportImportRows;");
            _context.Database.ExecuteSqlRaw("DELETE FROM DailyReportImportBatches;");
            _context.Database.ExecuteSqlRaw("DELETE FROM StockMovements WHERE RefType IN ('DailyReport', 'DailyReportRow', 'DailyReportReconcile', 'WithdrawBatch') OR MovementType = 'StockCountAdjust';");
            _context.Database.ExecuteSqlRaw("DELETE FROM PartUnits;");
            _context.Database.ExecuteSqlRaw("UPDATE PartStocks SET GoodQty = 0, RepairQty = 0, BadQty = 0;");
            _context.Database.ExecuteSqlRaw("UPDATE TicketPartLines SET ConfirmedQty = 0;");
            _context.Database.ExecuteSqlRaw("UPDATE WithdrawBatches SET ReturnStatus = 'เดินทาง' WHERE ReturnStatus = 'คืน';");

            _context.ChangeTracker.Clear();

            _audit.Log(User, "DailyReport", "RESET", "RESET_BASELINE", null, new { ResetBy = userName, Timestamp = DateTime.UtcNow });

            tx?.Commit();
            return Ok(new { message = "ล้างข้อมูล Daily Report และสต็อกอะไหล่เรียบร้อยแล้ว พร้อมสำหรับการนำเข้าไฟล์ตั้งต้น 03 ก.ย." });
        }
        catch (Exception ex)
        {
            tx?.Rollback();
            return BadRequest(new { message = $"เกิดข้อผิดพลาดในการล้างข้อมูล: {ex.Message}" });
        }
    }

    // GET /DailyReport/batches/{id} — projects into plain DTOs, never the raw entities. Returning
    // `batch`/`rows` straight from EF let the change tracker's automatic relationship fixup wire
    // DailyReportImportRow.Batch back to the same tracked DailyReportImportBatch instance (and
    // Batch.Rows back to all of them) — System.Text.Json then walked that graph outward, so one
    // batch of a few thousand rows serialized into a multi-gigabyte, multi-minute response. Every
    // other action in this controller (Preview/Confirm) already avoided this by returning RowResult
    // DTOs instead of entities; this was the one place that still didn't.
    [HttpGet("batches/{id}")]
    public IActionResult BatchDetail(int id)
    {
        var batch = _context.DailyReportImportBatches
            .Where(b => b.Id == id)
            .Select(b => new { b.Id, b.FileName, b.ImportedAt, b.ImportedBy, b.TotalRows })
            .FirstOrDefault();
        if (batch == null) return NotFound();

        var rows = _context.DailyReportImportRows
            .Where(r => r.BatchId == id)
            .OrderBy(r => r.RowIndex)
            .Select(r => new
            {
                r.Id, r.SourceSheet, r.RowIndex, r.PartNo, r.PartName, r.SerialNo, r.Qty,
                r.DhlStatus, r.Problem, r.CaseNo, r.FeName, r.MatchType,
                r.TicketId, r.WithdrawBatchId, r.PartUnitId, r.StockCredited, r.Undone, r.UndoneAt
            })
            .ToList();

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

                _stock.AdjustStock(row.PartNo, techLoc?.Id ?? 0, row.Qty, "Good", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถว import #{id}", serialNo: row.SerialNo);
                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, -row.Qty, row.DhlStatus == "GOOD" ? "Good" : "Repair", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถว import #{id}", serialNo: row.SerialNo);

                line.ConfirmedQty = Math.Max(0, line.ConfirmedQty - row.Qty);
                if (batch.ReturnStatus == "คืน") batch.ReturnStatus = "เดินทาง";

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
            else if (row.MatchType == "OutboundConfirmed")
            {
                // Stock was NOT deducted during import (already deducted by ApproveBatch).
                // Only revert ConfirmedQty on the withdraw line:
                if (row.WithdrawBatchId.HasValue)
                {
                    var line = _context.TicketPartLines.FirstOrDefault(l => l.WithdrawBatchId == row.WithdrawBatchId && l.LineType == "Withdraw" && l.PartNo == row.PartNo);
                    if (line != null) line.ConfirmedQty = Math.Max(0, line.ConfirmedQty - row.Qty);
                }

                if (row.PartUnitId.HasValue)
                {
                    var unit = _context.PartUnits.FirstOrDefault(u => u.Id == row.PartUnitId);
                    if (unit != null)
                    {
                        unit.Status = "InStock";
                        unit.LocationId = mainWh?.Id;
                    }
                }
            }
            else if (row.MatchType == "OutboundAutoTicket" || row.MatchType == "OutboundUnmatched")
            {
                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, "Good", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถวจ่ายออก #{id}", serialNo: row.SerialNo);
                _stock.AdjustStock(row.PartNo, techLoc?.Id ?? 0, -row.Qty, "Good", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถวจ่ายออก #{id}", serialNo: row.SerialNo);

                if (row.WithdrawBatchId.HasValue)
                {
                    var line = _context.TicketPartLines.FirstOrDefault(l => l.WithdrawBatchId == row.WithdrawBatchId && l.LineType == "Withdraw" && l.PartNo == row.PartNo);
                    if (line != null) _context.TicketPartLines.Remove(line);

                    var remainingLines = _context.TicketPartLines.Count(l => l.WithdrawBatchId == row.WithdrawBatchId && l.TicketPartLineId != (line != null ? line.TicketPartLineId : 0));
                    if (remainingLines == 0)
                    {
                        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == row.WithdrawBatchId);
                        if (batch != null) batch.Status = "Cancel";
                    }
                }

                if (row.PartUnitId.HasValue)
                {
                    var unit = _context.PartUnits.FirstOrDefault(u => u.Id == row.PartUnitId);
                    if (unit != null)
                    {
                        unit.Status = "InStock";
                        unit.LocationId = mainWh?.Id;
                    }
                }
            }
            else if (row.MatchType == "InboundRepaired" && row.StockCredited)
            {
                _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, -row.Qty, "Good", "UndoImport", "DailyReportRow", id.ToString(), userName, $"ย้อนกลับแถวรับเข้าซ่อม #{id}", serialNo: row.SerialNo);
                if (row.PartUnitId.HasValue)
                {
                    var unit = _context.PartUnits.FirstOrDefault(u => u.Id == row.PartUnitId);
                    if (unit != null) _context.PartUnits.Remove(unit);
                }
            }
            else if (row.MatchType == "Unmatched" && row.StockCredited)
            {
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

    // ── Data Models ──────────────────────────────────────────────────────────

    public class ParsedRow
    {
        public int RowIndex { get; set; }
        public string SourceSheet { get; set; } = "Return inbound";
        public string PartNo { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public string SerialNo { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string Status { get; set; } = string.Empty; // GOOD | BAD
        public string? Problem { get; set; }
        public string? CaseNo { get; set; }
        public string? FeName { get; set; }
        public string? ShippedFrom { get; set; }
        public string? CustomerSite { get; set; }
        public string? Address { get; set; }
        public DateTime? TxDate { get; set; }
        public DateTime? FeReceiveDate { get; set; }
    }

    public class ReconciliationItem
    {
        public string PartNo { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public int DhlGoodQty { get; set; }
        public int DhlBadQty { get; set; }
        public int DhlAvailableQty { get; set; } // mapped to DhlGoodQty for backward compatibility
        public int DhlMinQty { get; set; }
        public int DhlStock { get; set; }
        public int SystemGoodQty { get; set; }
        public int SystemRepairQty { get; set; }
        public int DiffGood { get; set; }
        public int DiffRepair { get; set; }
        public string Status { get; set; } = string.Empty; // MATCH | DIFF | NOT_IN_SYSTEM
    }

    public class ReconcileAdjustRequest
    {
        public List<ReconcileAdjustItem> Adjustments { get; set; } = new();
    }

    public class ReconcileAdjustItem
    {
        public string PartNo { get; set; } = string.Empty;
        public int TargetGood { get; set; }
        public int TargetRepair { get; set; }
        public string Reason { get; set; } = "นับสต็อกจริงคลัง DHL (Physical Cycle Count)";
    }

    public class RowResult
    {
        public int RowIndex { get; set; }
        public string SourceSheet { get; set; } = "Return inbound";
        public string PartNo { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public string SerialNo { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string DhlStatus { get; set; } = string.Empty;
        public string? Problem { get; set; }
        public string? CaseNo { get; set; }
        public string? FeName { get; set; }
        public string MatchType { get; set; } = string.Empty;
        public int? TicketId { get; set; }
        public int? WithdrawBatchId { get; set; }
        public string? ExternalTicketNo { get; set; }
        public int? PartUnitId { get; set; }
        public string? Note { get; set; }
        public bool StockCredited { get; set; }
    }

    public class Summary
    {
        public int ReturnConfirmed { get; set; }
        public int RepairCompleted { get; set; }
        public int StillInRepair { get; set; }
        public int Unmatched { get; set; }
        public int OutboundCount { get; set; }
        public int ExportRepairCount { get; set; }
        public int InboundRepairedCount { get; set; }
        public int PriorToBaselineCount { get; set; }
        public int AlreadyImportedCount { get; set; }
        public int TotalProcessed { get; set; }
        public int ReconcileMatchCount { get; set; }
        public int ReconcileDiffCount { get; set; }
        public int AutoCreatedTicketCount { get; set; }
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    private List<ParsedRow>? ParseFile(IFormFile? file, out string? error, out List<ReconciliationItem> reconciliation)
    {
        error = null;
        reconciliation = new List<ReconciliationItem>();
        if (file == null || file.Length == 0) { error = "กรุณาแนบไฟล์"; return null; }
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx") { error = "รองรับเฉพาะไฟล์ .xlsx"; return null; }

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);

            var rows = new List<ParsedRow>();

            // 1. Check for specific named sheets
            var hasReturnInbound = wb.Worksheets.Any(s => s.Name.Trim().Equals("Return inbound", StringComparison.OrdinalIgnoreCase));
            var hasOutboundOrder = wb.Worksheets.Any(s => s.Name.Trim().StartsWith("Outbound Order", StringComparison.OrdinalIgnoreCase) && !s.Name.Trim().Contains("Export", StringComparison.OrdinalIgnoreCase));
            var hasOutboundExport = wb.Worksheets.Any(s => s.Name.Trim().Replace(" ", "").StartsWith("OutboundOrdeExport", StringComparison.OrdinalIgnoreCase) || s.Name.Trim().Replace(" ", "").StartsWith("OutboundOrderExport", StringComparison.OrdinalIgnoreCase));
            var has24x7 = wb.Worksheets.Any(s => s.Name.Trim().Contains("24x7", StringComparison.OrdinalIgnoreCase));
            var hasInboundNormal = wb.Worksheets.Any(s => s.Name.Trim().StartsWith("Inbound normal", StringComparison.OrdinalIgnoreCase));
            var hasMinStock = wb.Worksheets.Any(s => s.Name.Trim().StartsWith("Minimum Stock", StringComparison.OrdinalIgnoreCase));

            // If none of the known multi-sheets exist, fallback to generic single-sheet scan for Return Inbound
            if (!hasReturnInbound && !hasOutboundOrder && !hasOutboundExport && !has24x7 && !hasInboundNormal)
            {
                var genericRows = ParseGenericReturnInbound(wb, out error);
                return genericRows;
            }

            // 1. Parse Outbound sheets first so tickets & withdraw batches exist before return matching
            if (hasOutboundOrder)
            {
                var ws = wb.Worksheets.First(s => s.Name.Trim().StartsWith("Outbound Order", StringComparison.OrdinalIgnoreCase) && !s.Name.Trim().Contains("Export", StringComparison.OrdinalIgnoreCase));
                ParseOutboundSheet(ws, rows, "Outbound Order ");
            }

            if (has24x7)
            {
                var ws = wb.Worksheets.First(s => s.Name.Trim().Contains("24x7", StringComparison.OrdinalIgnoreCase));
                ParseOutboundSheet(ws, rows, "24x7 ACTIVITY");
            }

            // 2. Parse "Return inbound" (technician returns matched against tickets)
            if (hasReturnInbound)
            {
                var ws = wb.Worksheets.First(s => s.Name.Trim().Equals("Return inbound", StringComparison.OrdinalIgnoreCase));
                ParseReturnInboundSheet(ws, rows);
            }

            // 3. Parse "Outbound Orde Export" (Parts sent to repair center / D1 Room Repair)
            if (hasOutboundExport)
            {
                var ws = wb.Worksheets.First(s => s.Name.Trim().Replace(" ", "").StartsWith("OutboundOrdeExport", StringComparison.OrdinalIgnoreCase) || s.Name.Trim().Replace(" ", "").StartsWith("OutboundOrderExport", StringComparison.OrdinalIgnoreCase));
                ParseExportRepairSheet(ws, rows);
            }

            // 4. Parse "Inbound normal" (repaired parts returning to Good stock)
            if (hasInboundNormal)
            {
                var ws = wb.Worksheets.First(s => s.Name.Trim().StartsWith("Inbound normal", StringComparison.OrdinalIgnoreCase));
                ParseInboundNormalSheet(ws, rows);
            }

            // 5. Parse "Minimum Stock" for reconciliation
            if (hasMinStock)
            {
                var ws = wb.Worksheets.First(s => s.Name.Trim().StartsWith("Minimum Stock", StringComparison.OrdinalIgnoreCase));
                ParseMinimumStockSheet(ws, reconciliation);
            }

            return rows;
        }
        catch (Exception ex)
        {
            error = $"อ่านไฟล์ไม่สำเร็จ: {ex.Message}";
            return null;
        }
    }

    private static void ParseReturnInboundSheet(IXLWorksheet ws, List<ParsedRow> rows)
    {
        int headerRow = -1, cPartNo = -1, cPartName = -1, cSerial = -1, cQty = -1, cStatus = -1, cProblem = -1, cCaseNo = -1, cFeName = -1;
        int cSysDate = -1, cWhDate = -1, cBookingDate = -1;
        var lastRowScan = Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1);
        var lastColScan = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        for (int r = 1; r <= lastRowScan; r++)
        {
            for (int c = 1; c <= lastColScan; c++)
            {
                var val = ws.Cell(r, c).GetString().Trim().Replace("\n", " ");
                if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { cPartNo = c; headerRow = r; }
                else if (val.Equals("Part Description", StringComparison.OrdinalIgnoreCase)) cPartName = c;
                else if (val.Replace("_", "").Equals("SERIALNUMBER", StringComparison.OrdinalIgnoreCase) || val.Equals("Serial Number", StringComparison.OrdinalIgnoreCase)) cSerial = c;
                else if (val.Equals("QTY", StringComparison.OrdinalIgnoreCase)) cQty = c;
                else if (val.Replace("\n", "").Contains("INVENTORY STATUS", StringComparison.OrdinalIgnoreCase) || val.Equals("Status", StringComparison.OrdinalIgnoreCase)) cStatus = c;
                else if (val.Equals("Problem", StringComparison.OrdinalIgnoreCase)) cProblem = c;
                else if (val.Equals("Case No", StringComparison.OrdinalIgnoreCase)) cCaseNo = c;
                else if (val.Equals("FE Name", StringComparison.OrdinalIgnoreCase)) cFeName = c;
                else if (val.Contains("System Received Date", StringComparison.OrdinalIgnoreCase)) cSysDate = c;
                else if (val.Contains("WH Received Date", StringComparison.OrdinalIgnoreCase)) cWhDate = c;
                else if (val.Contains("Booking Date", StringComparison.OrdinalIgnoreCase)) cBookingDate = c;
            }
            if (cPartNo >= 0 && cSerial >= 0 && cQty >= 0 && cStatus >= 0) break;
        }

        if (headerRow < 0 || cPartNo < 0) return;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = ws.Cell(r, cPartNo).GetString().Trim();
            if (string.IsNullOrWhiteSpace(partNo)) continue;

            var status = ws.Cell(r, cStatus).GetString().Trim().ToUpperInvariant();
            if (status != "GOOD" && status != "BAD") continue;

            var caseNo = cCaseNo > 0 ? ws.Cell(r, cCaseNo).GetString().Trim() : "";
            var feName = cFeName > 0 ? ws.Cell(r, cFeName).GetString().Trim() : "";
            DateTime? txDate = null;
            if (cSysDate > 0) txDate = TryParseCellDate(ws.Cell(r, cSysDate));
            if (txDate == null && cWhDate > 0) txDate = TryParseCellDate(ws.Cell(r, cWhDate));
            if (txDate == null && cBookingDate > 0) txDate = TryParseCellDate(ws.Cell(r, cBookingDate));

            rows.Add(new ParsedRow
            {
                RowIndex = r,
                SourceSheet = "Return inbound",
                PartNo = partNo,
                PartName = cPartName > 0 ? ws.Cell(r, cPartName).GetString().Trim() : "",
                SerialNo = ws.Cell(r, cSerial).GetString().Trim(),
                Qty = (int)(ws.Cell(r, cQty).GetValue<double?>() ?? 1),
                Status = status,
                Problem = cProblem > 0 ? ws.Cell(r, cProblem).GetString().Trim() : null,
                CaseNo = string.IsNullOrWhiteSpace(caseNo) ? null : caseNo,
                FeName = string.IsNullOrWhiteSpace(feName) ? null : feName,
                TxDate = txDate
            });
        }
    }

    private static void ParseOutboundSheet(IXLWorksheet ws, List<ParsedRow> rows, string sheetName)
    {
        int headerRow = -1, cPartNo = -1, cPartName = -1, cSerial = -1, cQty = -1, cStatus = -1, cCaseNo = -1, cFeName = -1, cDate = -1, cFeReceiveDate = -1;
        var lastRowScan = Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1);
        var lastColScan = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        for (int r = 1; r <= lastRowScan; r++)
        {
            for (int c = 1; c <= lastColScan; c++)
            {
                var val = ws.Cell(r, c).GetString().Trim().Replace("\n", " ");
                if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { cPartNo = c; headerRow = r; }
                else if (val.Equals("Part Description", StringComparison.OrdinalIgnoreCase)) cPartName = c;
                else if (val.Replace("_", "").Equals("SERIALNUMBER", StringComparison.OrdinalIgnoreCase) || val.Equals("Serial Number", StringComparison.OrdinalIgnoreCase)) cSerial = c;
                else if (val.Equals("QTY", StringComparison.OrdinalIgnoreCase)) cQty = c;
                else if (val.Equals("FE Name", StringComparison.OrdinalIgnoreCase)) cFeName = c;
                else if (val.Equals("Case No", StringComparison.OrdinalIgnoreCase)) cCaseNo = c;
                else if (val.Contains("Inventory Status", StringComparison.OrdinalIgnoreCase) || val.Equals("Status", StringComparison.OrdinalIgnoreCase)) cStatus = c;
                else if (val.Contains("FE Receive Date", StringComparison.OrdinalIgnoreCase) || val.Contains("Receive Date", StringComparison.OrdinalIgnoreCase)) cFeReceiveDate = c;
                else if (val.Contains("CREATION_DATE", StringComparison.OrdinalIgnoreCase) || val.Contains("ACTIVITY DATE", StringComparison.OrdinalIgnoreCase) || val.Equals("Order Date", StringComparison.OrdinalIgnoreCase))
                {
                    if (cDate < 0) cDate = c;
                }
            }
            if (cPartNo >= 0 && cQty >= 0) break;
        }

        if (headerRow < 0 || cPartNo < 0) return;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = ws.Cell(r, cPartNo).GetString().Trim();
            if (string.IsNullOrWhiteSpace(partNo)) continue;

            var serialNo = cSerial > 0 ? ws.Cell(r, cSerial).GetString().Trim() : "";
            var caseNo = cCaseNo > 0 ? ws.Cell(r, cCaseNo).GetString().Trim() : "";
            var feName = cFeName > 0 ? ws.Cell(r, cFeName).GetString().Trim() : "";
            var status = cStatus > 0 ? ws.Cell(r, cStatus).GetString().Trim().ToUpperInvariant() : "GOOD";
            if (string.IsNullOrWhiteSpace(status)) status = "GOOD";

            DateTime? txDate = cDate > 0 ? TryParseCellDate(ws.Cell(r, cDate)) : null;
            DateTime? feReceiveDate = cFeReceiveDate > 0 ? TryParseCellDate(ws.Cell(r, cFeReceiveDate)) : null;

            rows.Add(new ParsedRow
            {
                RowIndex = r,
                SourceSheet = sheetName,
                PartNo = partNo,
                PartName = cPartName > 0 ? ws.Cell(r, cPartName).GetString().Trim() : "",
                SerialNo = serialNo,
                Qty = (int)(cQty > 0 ? (ws.Cell(r, cQty).GetValue<double?>() ?? 1) : 1),
                Status = status,
                CaseNo = string.IsNullOrWhiteSpace(caseNo) ? null : caseNo,
                FeName = string.IsNullOrWhiteSpace(feName) ? null : feName,
                TxDate = txDate,
                FeReceiveDate = feReceiveDate
            });
        }
    }

    private static void ParseInboundNormalSheet(IXLWorksheet ws, List<ParsedRow> rows)
    {
        int headerRow = -1, cPartNo = -1, cPartName = -1, cSerial = -1, cQty = -1, cStatus = -1, cShipped = -1, cDate = -1;
        var lastRowScan = Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1);
        var lastColScan = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        for (int r = 1; r <= lastRowScan; r++)
        {
            for (int c = 1; c <= lastColScan; c++)
            {
                var val = ws.Cell(r, c).GetString().Trim().Replace("\n", " ");
                if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { cPartNo = c; headerRow = r; }
                else if (val.Equals("Part Description", StringComparison.OrdinalIgnoreCase)) cPartName = c;
                else if (val.Replace("_", "").Equals("SERIALNUMBER", StringComparison.OrdinalIgnoreCase) || val.Equals("Serial Number", StringComparison.OrdinalIgnoreCase)) cSerial = c;
                else if (val.Equals("QTY", StringComparison.OrdinalIgnoreCase)) cQty = c;
                else if (val.Contains("INVENTORY STATUS", StringComparison.OrdinalIgnoreCase) || val.Equals("Status", StringComparison.OrdinalIgnoreCase)) cStatus = c;
                else if (val.Contains("Shipped from", StringComparison.OrdinalIgnoreCase)) cShipped = c;
                else if (val.Contains("System Received Date", StringComparison.OrdinalIgnoreCase) || val.Contains("WH Received Date", StringComparison.OrdinalIgnoreCase) || val.Contains("Booking Date", StringComparison.OrdinalIgnoreCase))
                {
                    if (cDate < 0) cDate = c;
                }
            }
            if (cPartNo >= 0 && cQty >= 0) break;
        }

        if (headerRow < 0 || cPartNo < 0) return;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = ws.Cell(r, cPartNo).GetString().Trim();
            if (string.IsNullOrWhiteSpace(partNo)) continue;

            var serialNo = cSerial > 0 ? ws.Cell(r, cSerial).GetString().Trim() : "";
            var status = cStatus > 0 ? ws.Cell(r, cStatus).GetString().Trim().ToUpperInvariant() : "GOOD";
            var shippedFrom = cShipped > 0 ? ws.Cell(r, cShipped).GetString().Trim() : null;
            DateTime? txDate = cDate > 0 ? TryParseCellDate(ws.Cell(r, cDate)) : null;

            rows.Add(new ParsedRow
            {
                RowIndex = r,
                SourceSheet = "Inbound normal",
                PartNo = partNo,
                PartName = cPartName > 0 ? ws.Cell(r, cPartName).GetString().Trim() : "",
                SerialNo = serialNo,
                Qty = (int)(cQty > 0 ? (ws.Cell(r, cQty).GetValue<double?>() ?? 1) : 1),
                Status = status,
                ShippedFrom = shippedFrom,
                TxDate = txDate
            });
        }
    }

    private static void ParseExportRepairSheet(IXLWorksheet ws, List<ParsedRow> rows)
    {
        int headerRow = -1, cPartNo = -1, cPartName = -1, cSerial = -1, cQty = -1, cStatus = -1, cCaseNo = -1, cFeName = -1, cDate = -1, cCustSite = -1, cAddress = -1;
        var lastRowScan = Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1);
        var lastColScan = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        for (int r = 1; r <= lastRowScan; r++)
        {
            for (int c = 1; c <= lastColScan; c++)
            {
                var val = ws.Cell(r, c).GetString().Trim().Replace("\n", " ");
                if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { cPartNo = c; headerRow = r; }
                else if (val.Equals("Part Description", StringComparison.OrdinalIgnoreCase)) cPartName = c;
                else if (val.Replace("_", "").Equals("SERIALNUMBER", StringComparison.OrdinalIgnoreCase) || val.Equals("Serial Number", StringComparison.OrdinalIgnoreCase)) cSerial = c;
                else if (val.Equals("QTY", StringComparison.OrdinalIgnoreCase)) cQty = c;
                else if (val.Equals("FE Name", StringComparison.OrdinalIgnoreCase)) cFeName = c;
                else if (val.Equals("Case No", StringComparison.OrdinalIgnoreCase)) cCaseNo = c;
                else if (val.Contains("Customer site", StringComparison.OrdinalIgnoreCase)) cCustSite = c;
                else if (val.Equals("Address", StringComparison.OrdinalIgnoreCase)) cAddress = c;
                else if (val.Contains("Inventory Status", StringComparison.OrdinalIgnoreCase) || val.Equals("Status", StringComparison.OrdinalIgnoreCase)) cStatus = c;
                else if (val.Contains("CREATION_DATE", StringComparison.OrdinalIgnoreCase) || val.Contains("ACTIVITY DATE", StringComparison.OrdinalIgnoreCase) || val.Equals("Order Date", StringComparison.OrdinalIgnoreCase))
                {
                    if (cDate < 0) cDate = c;
                }
            }
            if (cPartNo >= 0 && cQty >= 0) break;
        }

        if (headerRow < 0 || cPartNo < 0) return;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = ws.Cell(r, cPartNo).GetString().Trim();
            if (string.IsNullOrWhiteSpace(partNo)) continue;

            var serialNo = cSerial > 0 ? ws.Cell(r, cSerial).GetString().Trim() : "";
            var caseNo = cCaseNo > 0 ? ws.Cell(r, cCaseNo).GetString().Trim() : "";
            var feName = cFeName > 0 ? ws.Cell(r, cFeName).GetString().Trim() : "";
            var status = cStatus > 0 ? ws.Cell(r, cStatus).GetString().Trim().ToUpperInvariant() : "BAD";
            if (string.IsNullOrWhiteSpace(status)) status = "BAD";
            var custSite = cCustSite > 0 ? ws.Cell(r, cCustSite).GetString().Trim() : "";
            var address = cAddress > 0 ? ws.Cell(r, cAddress).GetString().Trim() : "";

            DateTime? txDate = cDate > 0 ? TryParseCellDate(ws.Cell(r, cDate)) : null;

            rows.Add(new ParsedRow
            {
                RowIndex = r,
                SourceSheet = "Outbound Orde Export",
                PartNo = partNo,
                PartName = cPartName > 0 ? ws.Cell(r, cPartName).GetString().Trim() : "",
                SerialNo = serialNo,
                Qty = (int)(cQty > 0 ? (ws.Cell(r, cQty).GetValue<double?>() ?? 1) : 1),
                Status = status,
                CaseNo = string.IsNullOrWhiteSpace(caseNo) ? null : caseNo,
                FeName = string.IsNullOrWhiteSpace(feName) ? null : feName,
                CustomerSite = string.IsNullOrWhiteSpace(custSite) ? null : custSite,
                Address = string.IsNullOrWhiteSpace(address) ? null : address,
                TxDate = txDate
            });
        }
    }

    private static void ParseMinimumStockSheet(IXLWorksheet ws, List<ReconciliationItem> items)
    {
        int headerRow = -1, cPartNo = -1, cPartName = -1, cAvail = -1, cMin = -1, cStock = -1, cStatus = -1;
        var lastRowScan = Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1);
        var lastColScan = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        for (int r = 1; r <= lastRowScan; r++)
        {
            for (int c = 1; c <= lastColScan; c++)
            {
                var val = ws.Cell(r, c).GetString().Trim().Replace("\n", " ");
                if (val.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) { cPartNo = c; headerRow = r; }
                else if (val.Equals("Part Description", StringComparison.OrdinalIgnoreCase)) cPartName = c;
                else if (val.Equals("AVAILABLE_QTY", StringComparison.OrdinalIgnoreCase)) cAvail = c;
                else if (val.Equals("MIN QTY", StringComparison.OrdinalIgnoreCase)) cMin = c;
                else if (val.Equals("STOCK", StringComparison.OrdinalIgnoreCase)) cStock = c;
                else if (val.Contains("INVENTORY_STS", StringComparison.OrdinalIgnoreCase) || val.Contains("INVENTORY STATUS", StringComparison.OrdinalIgnoreCase) || val.Equals("Status", StringComparison.OrdinalIgnoreCase)) cStatus = c;
            }
            if (cPartNo >= 0 && cAvail >= 0) break;
        }

        if (headerRow < 0 || cPartNo < 0) return;

        var itemsByPart = new Dictionary<string, ReconciliationItem>(StringComparer.OrdinalIgnoreCase);

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = ws.Cell(r, cPartNo).GetString().Trim();
            if (string.IsNullOrWhiteSpace(partNo)) continue;

            var rawStatus = cStatus > 0 ? ws.Cell(r, cStatus).GetString().Trim().ToUpperInvariant() : "GOOD";
            var isBad = rawStatus.Contains("BAD");
            var availQty = (int)(cAvail > 0 ? (ws.Cell(r, cAvail).GetValue<double?>() ?? 0) : 0);
            var minQty = (int)(cMin > 0 ? (ws.Cell(r, cMin).GetValue<double?>() ?? 0) : 0);
            var stockQty = (int)(cStock > 0 ? (ws.Cell(r, cStock).GetValue<double?>() ?? 0) : 0);
            var partName = cPartName > 0 ? ws.Cell(r, cPartName).GetString().Trim() : "";

            if (!itemsByPart.TryGetValue(partNo, out var item))
            {
                item = new ReconciliationItem
                {
                    PartNo = partNo,
                    PartName = partName,
                    DhlMinQty = minQty,
                    DhlStock = stockQty
                };
                itemsByPart[partNo] = item;
            }

            if (!string.IsNullOrWhiteSpace(partName) && string.IsNullOrWhiteSpace(item.PartName))
                item.PartName = partName;
            if (minQty > 0 && item.DhlMinQty == 0)
                item.DhlMinQty = minQty;

            if (isBad)
                item.DhlBadQty += availQty;
            else
                item.DhlGoodQty += availQty;

            item.DhlAvailableQty = item.DhlGoodQty;
        }

        items.AddRange(itemsByPart.Values);
    }

    private static List<ParsedRow>? ParseGenericReturnInbound(XLWorkbook wb, out string? error)
    {
        error = null;
        IXLWorksheet? ws = null;
        int headerRow = -1, cPartNo = -1, cPartName = -1, cSerial = -1, cQty = -1, cStatus = -1, cProblem = -1, cCaseNo = -1;
        int cSysDate = -1, cWhDate = -1, cBookingDate = -1;

        var preferred = wb.Worksheets.Contains("Return inbound")
            ? new[] { wb.Worksheet("Return inbound") }.Concat(wb.Worksheets)
            : wb.Worksheets.AsEnumerable();

        foreach (var candidate in preferred)
        {
            int r1 = -1, p1 = -1, pn1 = -1, s1 = -1, q1 = -1, st1 = -1, pr1 = -1, cn1 = -1;
            int sdt = -1, wdt = -1, bdt = -1;
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
                    else if (val.Equals("Case No", StringComparison.OrdinalIgnoreCase)) cn1 = c;
                    else if (val.Contains("System Received Date", StringComparison.OrdinalIgnoreCase)) sdt = c;
                    else if (val.Contains("WH Received Date", StringComparison.OrdinalIgnoreCase)) wdt = c;
                    else if (val.Contains("Booking Date", StringComparison.OrdinalIgnoreCase)) bdt = c;
                }
            if (p1 >= 0 && s1 >= 0 && q1 >= 0 && st1 >= 0)
            {
                ws = candidate; headerRow = r1; cPartNo = p1; cPartName = pn1; cSerial = s1; cQty = q1; cStatus = st1; cProblem = pr1; cCaseNo = cn1;
                cSysDate = sdt; cWhDate = wdt; cBookingDate = bdt;
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
            if (status != "GOOD" && status != "BAD") continue;

            var caseNo = cCaseNo > 0 ? ws.Cell(r, cCaseNo).GetString().Trim() : "";
            DateTime? txDate = null;
            if (cSysDate > 0) txDate = TryParseCellDate(ws.Cell(r, cSysDate));
            if (txDate == null && cWhDate > 0) txDate = TryParseCellDate(ws.Cell(r, cWhDate));
            if (txDate == null && cBookingDate > 0) txDate = TryParseCellDate(ws.Cell(r, cBookingDate));

            rows.Add(new ParsedRow
            {
                RowIndex = r,
                SourceSheet = "Return inbound",
                PartNo = partNo,
                PartName = cPartName > 0 ? ws.Cell(r, cPartName).GetString().Trim() : "",
                SerialNo = ws.Cell(r, cSerial).GetString().Trim(),
                Qty = (int)(ws.Cell(r, cQty).GetValue<double?>() ?? 1),
                Status = status,
                Problem = cProblem > 0 ? ws.Cell(r, cProblem).GetString().Trim() : null,
                CaseNo = string.IsNullOrWhiteSpace(caseNo) ? null : caseNo,
                TxDate = txDate
            });
        }
        return rows;
    }

    private static DateTime? TryParseCellDate(IXLCell? cell)
    {
        if (cell == null || cell.IsEmpty()) return null;
        try
        {
            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime();
            if (cell.DataType == XLDataType.Number)
            {
                var num = cell.GetDouble();
                if (num > 30000 && num < 60000)
                    return DateTime.FromOADate(num);
            }
            var str = cell.GetString().Trim();
            if (string.IsNullOrEmpty(str)) return null;
            if (DateTime.TryParse(str, out var dt)) return dt;
            if (DateTime.TryParseExact(str, new[] {
                "dd-MMM-yyyy", "dd-MMM-yyyy HH:mm:ss", "d-MMM-yyyy",
                "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy", "d/M/yyyy"
            }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
                return dt;
        }
        catch { }
        return null;
    }

    // ── Matching & Processing ────────────────────────────────────────────────

    private (List<RowResult> Rows, Summary Summary) Process(List<ParsedRow> rows, List<ReconciliationItem> reconciliation, bool commit, string userName, bool syncReconcile = false)
    {
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "DHL-BKK");
        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");
        var ratWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT" || l.LocationType == "RATCHABURANA");
        var partsByNo = _context.Parts.ToList().GroupBy(p => p.PartNo.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var stocksByPartId = mainWh != null ? _context.PartStocks.Where(s => s.LocationId == mainWh.Id).ToDictionary(s => s.PartId, s => s) : new();

        var openReturnLines = _context.TicketPartLines
            .Include(l => l.Ticket)
            .Include(l => l.WithdrawBatch)
            .Where(l => l.LineType == "Return" && l.WithdrawBatch!.ReturnStatus == "เดินทาง" && l.WithdrawBatch.ReturnAddress != null && l.ConfirmedQty < l.Quantity)
            .OrderBy(l => l.WithdrawBatch!.UpdatedAt)
            .ToList();
        var remainingReturn = openReturnLines.ToDictionary(l => l.TicketPartLineId, l => l.Quantity - l.ConfirmedQty);

        var openWithdrawLines = _context.TicketPartLines
            .Include(l => l.Ticket)
            .Include(l => l.WithdrawBatch)
            .Where(l => l.LineType == "Withdraw" && l.WithdrawBatch != null && (l.WithdrawBatch.Status == "รอ" || l.WithdrawBatch.Status == "รออะไหล่" || l.WithdrawBatch.Status == "รอส่งเมล DHL" || l.WithdrawBatch.Status == "เดินทาง") && l.ConfirmedQty < l.Quantity)
            .OrderBy(l => l.WithdrawBatch!.UpdatedAt)
            .ToList();
        var remainingWithdraw = openWithdrawLines.ToDictionary(l => l.TicketPartLineId, l => l.Quantity - l.ConfirmedQty);



        var serialStatusOverlay = new Dictionary<string, string>();
        var results = new List<RowResult>();
        var summary = new Summary();

        foreach (var row in rows)
        {
            summary.TotalProcessed++;

            // 1. Check if row is prior to baseline snapshot (03 Sep 2026)
            // For non-outbound sheets (Return / Inbound / Export), register S/N into PartUnits without inflating stock counts.
            // For Tech Outbound sheets, allow creating Ticket and WithdrawBatch (ใบเบิก) but skip central warehouse deduction so stock doesn't double-deduct.
            var isPriorToBaseline = row.TxDate.HasValue && row.TxDate.Value <= BaselineDate;
            var isExportRepairSheet = row.SourceSheet.Contains("Export", StringComparison.OrdinalIgnoreCase);
            var isTechOutboundSheet = (row.SourceSheet.StartsWith("Outbound", StringComparison.OrdinalIgnoreCase) || row.SourceSheet.Contains("24x7")) && !isExportRepairSheet;

            if (isPriorToBaseline && !isTechOutboundSheet)
            {
                summary.PriorToBaselineCount++;
                int? unitId = null;
                if (!string.IsNullOrWhiteSpace(row.SerialNo) && partsByNo.TryGetValue(row.PartNo, out var bPart))
                {
                    var unit = _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo);
                    var targetLocId = isExportRepairSheet ? (ratWh?.Id ?? mainWh?.Id) : mainWh?.Id;
                    if (unit == null)
                    {
                        if (commit)
                        {
                            unit = new PartUnit { SerialNo = row.SerialNo, PartId = bPart.Id };
                            unit.Status = (row.Status == "BAD" || isExportRepairSheet) ? "InRepair" : "InStock";
                            unit.Condition = (row.Status == "BAD" || isExportRepairSheet) ? "Bad" : "Good";
                            unit.LocationId = targetLocId;
                            _context.PartUnits.Add(unit);
                            _context.SaveChanges();
                            unitId = unit.Id;
                        }
                    }
                    else
                    {
                        if (commit && isExportRepairSheet)
                        {
                            unit.Status = "InRepair";
                            unit.Condition = "Bad";
                            unit.LocationId = targetLocId;
                            _context.SaveChanges();
                        }
                        unitId = unit.Id;
                    }
                }

                results.Add(new RowResult
                {
                    RowIndex = row.RowIndex,
                    SourceSheet = row.SourceSheet,
                    PartNo = row.PartNo,
                    PartName = row.PartName,
                    SerialNo = row.SerialNo,
                    Qty = row.Qty,
                    DhlStatus = row.Status,
                    Problem = row.Problem,
                    CaseNo = row.CaseNo,
                    FeName = row.FeName,
                    MatchType = "PriorToBaseline",
                    PartUnitId = unitId,
                    Note = isExportRepairSheet
                        ? "ส่งออกไปศูนย์ซ่อม D1 Room Repair (คลังราษฎร์บูรณะ) ก่อนวันตั้งต้น — บันทึกตำแหน่งอะไหล่แล้ว"
                        : "เกิดขึ้นก่อนวันตั้งต้น (03 ก.ย. 2026) — บันทึก S/N ในระบบแล้ว ไม่ปรับสต็อกซ้ำ"
                });
                continue;
            }

            var currentUnitStatus = string.IsNullOrWhiteSpace(row.SerialNo)
                ? null
                : serialStatusOverlay.TryGetValue(row.SerialNo, out var overlaid)
                    ? overlaid
                    : _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo)?.Status;

            // If an Outbound item was already linked to an existing Ticket/WithdrawBatch, or already issued/imported, do not process again
            var isAlreadyInTicket = !string.IsNullOrWhiteSpace(row.SerialNo)
                ? (_context.TicketPartLines.Any(l => l.SerialNo == row.SerialNo && l.LineType == "Withdraw")
                   || currentUnitStatus == "Issued"
                   || _context.DailyReportImportRows.Any(r => r.SerialNo == row.SerialNo && !r.Undone && (r.MatchType == "OutboundConfirmed" || r.MatchType == "OutboundUnmatched" || r.MatchType == "OutboundAutoTicket")))
                : (!string.IsNullOrWhiteSpace(row.CaseNo) && (
                    _context.TicketPartLines.Any(l => l.Ticket != null && l.Ticket.ExternalTicketNo == row.CaseNo && l.PartNo == row.PartNo && l.LineType == "Withdraw")
                    || _context.DailyReportImportRows.Any(r => r.CaseNo == row.CaseNo && r.PartNo == row.PartNo && !r.Undone && (r.MatchType == "OutboundConfirmed" || r.MatchType == "OutboundUnmatched" || r.MatchType == "OutboundAutoTicket"))
                ));

            if (isTechOutboundSheet && isAlreadyInTicket)
            {
                if (row.FeReceiveDate.HasValue && commit)
                {
                    var existingLine = !string.IsNullOrWhiteSpace(row.SerialNo)
                        ? _context.TicketPartLines.Include(l => l.WithdrawBatch).FirstOrDefault(l => l.SerialNo == row.SerialNo && l.LineType == "Withdraw")
                        : (!string.IsNullOrWhiteSpace(row.CaseNo) ? _context.TicketPartLines.Include(l => l.WithdrawBatch).FirstOrDefault(l => l.Ticket != null && l.Ticket.ExternalTicketNo == row.CaseNo && l.PartNo == row.PartNo && l.LineType == "Withdraw") : null);

                    if (existingLine?.WithdrawBatch != null && existingLine.WithdrawBatch.Status == "เดินทาง")
                    {
                        existingLine.WithdrawBatch.Status = "เบิก";
                        existingLine.WithdrawBatch.UpdatedAt = DateTime.Now;
                        _context.SaveChanges();
                    }
                }

                summary.AlreadyImportedCount++;
                results.Add(new RowResult
                {
                    RowIndex = row.RowIndex,
                    SourceSheet = row.SourceSheet,
                    PartNo = row.PartNo,
                    PartName = row.PartName,
                    SerialNo = row.SerialNo,
                    Qty = row.Qty,
                    DhlStatus = row.Status,
                    Problem = row.Problem,
                    CaseNo = row.CaseNo,
                    FeName = row.FeName,
                    MatchType = "AlreadyImported",
                    Note = !string.IsNullOrWhiteSpace(row.SerialNo)
                        ? "SN นี้ถูกบันทึกจ่ายออกไปแล้ว — ไม่หักสต็อกซ้ำ"
                        : "รายการอะไหล่นี้ (ไม่มี SN) ถูกบันทึกใน Case นี้ไปแล้ว — ไม่หักสต็อกซ้ำ"
                });
                continue;
            }

            // ── SHEET BRANCH: Outbound Order / 24x7 ACTIVITY ──
            if (isTechOutboundSheet)
            {
                summary.OutboundCount++;
                var matchedByCaseNo = !string.IsNullOrWhiteSpace(row.CaseNo);
                var outLine = matchedByCaseNo
                    ? openWithdrawLines.FirstOrDefault(l => (l.PartNo == row.PartNo || l.OriginalPartNo == row.PartNo) && l.Ticket!.ExternalTicketNo == row.CaseNo && remainingWithdraw.GetValueOrDefault(l.TicketPartLineId) > 0)
                    : null;

                // Smart Fallback 1: Match by PartNo + FE Name if CaseNo not matched
                var matchedByFeFallback = false;
                if (outLine == null && !string.IsNullOrWhiteSpace(row.FeName))
                {
                    outLine = openWithdrawLines.FirstOrDefault(l => (l.PartNo == row.PartNo || l.OriginalPartNo == row.PartNo) && (l.Ticket!.TechName == row.FeName || l.WithdrawBatch!.EmployeeCode == row.FeName) && remainingWithdraw.GetValueOrDefault(l.TicketPartLineId) > 0);
                    if (outLine != null) matchedByFeFallback = true;
                }

                // Fallback 2: Match by PartNo only if row.CaseNo was blank
                if (outLine == null && !matchedByCaseNo)
                {
                    outLine = openWithdrawLines.FirstOrDefault(l => (l.PartNo == row.PartNo || l.OriginalPartNo == row.PartNo) && remainingWithdraw.GetValueOrDefault(l.TicketPartLineId) > 0);
                }

                int? unitId = null;
                if (outLine != null)
                {
                    remainingWithdraw[outLine.TicketPartLineId] = Math.Max(0, remainingWithdraw[outLine.TicketPartLineId] - row.Qty);
                    if (!string.IsNullOrWhiteSpace(row.SerialNo))
                        serialStatusOverlay[row.SerialNo] = "Issued";

                    var isPreApproved = outLine.WithdrawBatch?.Status is "รอส่งเมล DHL" or "เดินทาง";

                    if (commit)
                    {
                        // Deduct DHL-BKK ONLY IF not prior to baseline AND NOT already deducted at approve time
                        if (!isPriorToBaseline && !isPreApproved)
                        {
                            var currentMainStock = (mainWh != null && partsByNo.TryGetValue(row.PartNo, out var pMain))
                                ? (_context.PartStocks.FirstOrDefault(s => s.LocationId == mainWh.Id && s.PartId == pMain.Id)?.GoodQty ?? 0)
                                : 0;
                            var mainDeduct = Math.Min(currentMainStock, row.Qty);
                            if (mainDeduct > 0 && mainWh != null)
                            {
                                _stock.AdjustStock(row.PartNo, mainWh.Id, -mainDeduct, "Good", "Withdraw", "WithdrawBatch", outLine.WithdrawBatchId?.ToString(), userName, $"จ่ายออกผ่าน Daily Report SN {row.SerialNo}", serialNo: row.SerialNo);
                            }
                        }
                        _stock.AdjustStock(row.PartNo, techLoc?.Id ?? 0, row.Qty, "Good", "Withdraw", "WithdrawBatch", outLine.WithdrawBatchId?.ToString(), userName, $"รับอะไหล่จากคลังผ่าน Daily Report SN {row.SerialNo}", serialNo: row.SerialNo);

                        if (!string.IsNullOrWhiteSpace(row.SerialNo) && partsByNo.TryGetValue(row.PartNo, out var part))
                        {
                            var unit = _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo);
                            if (unit == null)
                            {
                                unit = new PartUnit { SerialNo = row.SerialNo, PartId = part.Id };
                                _context.PartUnits.Add(unit);
                            }
                            unit.Status = "Issued";
                            unit.LocationId = techLoc?.Id;
                            _context.SaveChanges();
                            unitId = unit.Id;
                        }

                        if (!string.IsNullOrWhiteSpace(row.SerialNo) && string.IsNullOrWhiteSpace(outLine.SerialNo))
                        {
                            outLine.SerialNo = row.SerialNo;
                        }

                        outLine.ConfirmedQty += row.Qty;
                        var allConfirmed = _context.TicketPartLines
                            .Where(l => l.WithdrawBatchId == outLine.WithdrawBatchId && l.LineType == "Withdraw")
                            .All(l => l.TicketPartLineId == outLine.TicketPartLineId ? outLine.ConfirmedQty >= l.Quantity : l.ConfirmedQty >= l.Quantity);
                        if (allConfirmed && outLine.WithdrawBatch != null)
                        {
                            if (outLine.WithdrawBatch.Status is "รอ" or "รออะไหล่")
                            {
                                outLine.WithdrawBatch.ApproverName = "DHL Daily Report (Auto-Approve)";
                                outLine.WithdrawBatch.ApprovedAt = DateTime.Now;
                            }
                            var hasReceived = row.FeReceiveDate.HasValue || isPriorToBaseline;
                            outLine.WithdrawBatch.Status = hasReceived ? "เบิก" : "เดินทาง";
                            if (!hasReceived && outLine.WithdrawBatch.EmailSentAt == null)
                                outLine.WithdrawBatch.EmailSentAt = row.TxDate ?? DateTime.Now;
                            outLine.WithdrawBatch.UpdatedAt = DateTime.Now;
                        }
                        _context.SaveChanges();
                    }

                    var matchNote = isPreApproved
                        ? "จ่ายออกตามใบเบิกเดิม — บันทึก S/N และอัปเดตสถานะเดินทาง (ไม่ตัดสต็อกซ้ำ)"
                        : "จ่ายออกตามใบเบิกเดิม (อนุมัติอัตโนมัติจาก Daily Report) — บันทึก S/N และหักสต็อกคลังกลาง";

                    if (matchedByFeFallback)
                    {
                        matchNote += $" [จับคู่ด้วยช่าง {row.FeName} แทน Case No]";
                    }

                    results.Add(new RowResult
                    {
                        RowIndex = row.RowIndex,
                        SourceSheet = row.SourceSheet,
                        PartNo = row.PartNo,
                        PartName = row.PartName,
                        SerialNo = row.SerialNo,
                        Qty = row.Qty,
                        DhlStatus = row.Status,
                        Problem = row.Problem,
                        CaseNo = row.CaseNo,
                        FeName = row.FeName,
                        MatchType = "OutboundConfirmed",
                        TicketId = outLine.TicketId,
                        WithdrawBatchId = outLine.WithdrawBatchId,
                        ExternalTicketNo = outLine.Ticket?.ExternalTicketNo,
                        PartUnitId = unitId,
                        StockCredited = false,
                        Note = matchNote
                    });
                }
                else
                {
                    // UNMATCHED OUTBOUND — Do NOT auto-create Ticket. Record as direct outbound stock movement.
                    if (!string.IsNullOrWhiteSpace(row.SerialNo))
                        serialStatusOverlay[row.SerialNo] = "Issued";

                    unitId = null;
                    if (partsByNo.TryGetValue(row.PartNo, out var part))
                    {
                        if (commit)
                        {
                            // Adjust Stock: If prior to baseline (03 Sep), stock in DHL-BKK was already counted post-outbound.
                            // So do not deduct DHL-BKK to avoid double-deducting baseline stock.
                            // Only deduct DHL-BKK if this is a new outbound occurring after baseline.
                            if (!isPriorToBaseline)
                            {
                                var currentMainStock = (mainWh != null && partsByNo.TryGetValue(row.PartNo, out var pMain))
                                    ? (_context.PartStocks.FirstOrDefault(s => s.LocationId == mainWh.Id && s.PartId == pMain.Id)?.GoodQty ?? 0)
                                    : 0;
                                var mainDeduct = Math.Min(currentMainStock, row.Qty);
                                if (mainDeduct > 0 && mainWh != null)
                                {
                                    _stock.AdjustStock(row.PartNo, mainWh.Id, -mainDeduct, "Good", "Withdraw", "DailyReportRow", row.RowIndex.ToString(), userName, $"จ่ายออกจากคลังกลาง (ไม่ผูกใบเบิก) SN {row.SerialNo}", serialNo: row.SerialNo);
                                }
                            }
                            _stock.AdjustStock(row.PartNo, techLoc?.Id ?? 0, row.Qty, "Good", "Withdraw", "DailyReportRow", row.RowIndex.ToString(), userName, $"ส่งมอบให้ช่าง {row.FeName} (ไม่ผูกใบเบิก) SN {row.SerialNo}", serialNo: row.SerialNo);

                            if (!string.IsNullOrWhiteSpace(row.SerialNo))
                            {
                                var unit = _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo);
                                if (unit == null)
                                {
                                    unit = new PartUnit { SerialNo = row.SerialNo, PartId = part.Id };
                                    _context.PartUnits.Add(unit);
                                }
                                unit.Status = "Issued";
                                unit.LocationId = techLoc?.Id;
                                _context.SaveChanges();
                                unitId = unit.Id;
                            }
                            _context.SaveChanges();
                        }

                        var note = isPriorToBaseline
                            ? $"จ่ายออกก่อนวันตั้งต้น (3 ก.ย. ช่าง {row.FeName ?? "Technician"}) — ไม่หักสต็อกคลังกลางซ้ำ"
                            : $"จ่ายออกจากคลัง DHL ให้ช่าง {row.FeName ?? "Technician"} (ตัดสต็อกโดยตรง ไม่เปิดใบเบิก)";

                        results.Add(new RowResult
                        {
                            RowIndex = row.RowIndex,
                            SourceSheet = row.SourceSheet,
                            PartNo = row.PartNo,
                            PartName = row.PartName,
                            SerialNo = row.SerialNo,
                            Qty = row.Qty,
                            DhlStatus = row.Status,
                            Problem = row.Problem,
                            CaseNo = row.CaseNo,
                            FeName = row.FeName,
                            MatchType = "OutboundUnmatched",
                            PartUnitId = unitId,
                            StockCredited = !isPriorToBaseline,
                            Note = note
                        });
                    }
                    else
                    {
                        summary.Unmatched++;
                        results.Add(new RowResult
                        {
                            RowIndex = row.RowIndex,
                            SourceSheet = row.SourceSheet,
                            PartNo = row.PartNo,
                            PartName = row.PartName,
                            SerialNo = row.SerialNo,
                            Qty = row.Qty,
                            DhlStatus = row.Status,
                            Problem = row.Problem,
                            CaseNo = row.CaseNo,
                            FeName = row.FeName,
                            MatchType = "OutboundUnmatched",
                            Note = $"ไม่พบรหัสอะไหล่ {row.PartNo} ในระบบ — ข้ามการปรับสต็อก"
                        });
                    }
                }
                continue;
            }

            // ── SHEET BRANCH: Outbound Orde Export (Parts sent to repair center: D1 Room Repair / WH-RAT) ──
            if (isExportRepairSheet)
            {
                summary.ExportRepairCount++;
                if (!string.IsNullOrWhiteSpace(row.SerialNo))
                    serialStatusOverlay[row.SerialNo] = "InRepair";

                int? unitId = null;
                var unit = !string.IsNullOrWhiteSpace(row.SerialNo) ? _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo) : null;
                var isAlreadyAtRat = unit != null && unit.LocationId == ratWh?.Id;

                if (commit)
                {
                    if (!string.IsNullOrWhiteSpace(row.SerialNo) && partsByNo.TryGetValue(row.PartNo, out var expPart))
                    {
                        if (unit == null)
                        {
                            unit = new PartUnit { SerialNo = row.SerialNo, PartId = expPart.Id };
                            _context.PartUnits.Add(unit);
                        }
                        unit.Status = "InRepair";
                        unit.Condition = "Bad";
                        unit.LocationId = ratWh?.Id ?? mainWh?.Id;
                        _context.SaveChanges();
                        unitId = unit.Id;
                    }

                    if (!isPriorToBaseline && !isAlreadyAtRat)
                    {
                        var targetPartId = partsByNo.TryGetValue(row.PartNo, out var p) ? p.Id : 0;
                        var mainStock = _context.PartStocks.Local.FirstOrDefault(s => s.LocationId == (mainWh != null ? mainWh.Id : 0) && s.PartId == targetPartId)
                                     ?? _context.PartStocks.FirstOrDefault(s => s.LocationId == (mainWh != null ? mainWh.Id : 0) && s.PartId == targetPartId);
                        var currentRepairStock = mainStock?.RepairQty ?? 0;
                        var repairDeduct = Math.Min(currentRepairStock, row.Qty);
                        if (repairDeduct > 0 && mainWh != null)
                        {
                            _stock.AdjustStock(row.PartNo, mainWh.Id, -repairDeduct, "Repair", "StockTransfer", "DailyReportRow", null, userName, $"ส่งออกไปศูนย์ซ่อม D1 Room Repair (คลังราษฎร์บูรณะ) SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unitId);
                        }
                        if (ratWh != null)
                        {
                            _stock.AdjustStock(row.PartNo, ratWh.Id, row.Qty, "Repair", "StockTransfer", "DailyReportRow", null, userName, $"รับเข้าศูนย์ซ่อม D1 Room Repair (คลังราษฎร์บูรณะ) SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unitId);
                        }
                    }
                }

                results.Add(new RowResult
                {
                    RowIndex = row.RowIndex,
                    SourceSheet = row.SourceSheet,
                    PartNo = row.PartNo,
                    PartName = row.PartName,
                    SerialNo = row.SerialNo,
                    Qty = row.Qty,
                    DhlStatus = row.Status,
                    Problem = row.Problem,
                    CaseNo = row.CaseNo,
                    FeName = row.FeName,
                    MatchType = "ExportRepair",
                    PartUnitId = unitId ?? unit?.Id,
                    StockCredited = !isPriorToBaseline && !isAlreadyAtRat,
                    Note = "ส่งออกไปศูนย์ซ่อม D1 Room Repair (คลังราษฎร์บูรณะ)"
                });
                continue;
            }

            // ── SHEET BRANCH: Inbound normal (Repaired stock returning) ──
            if (row.SourceSheet.StartsWith("Inbound normal", StringComparison.OrdinalIgnoreCase))
            {
                if (currentUnitStatus == "InRepair")
                {
                    summary.RepairCompleted++;
                    if (!string.IsNullOrWhiteSpace(row.SerialNo))
                        serialStatusOverlay[row.SerialNo] = "InStock";

                    int? unitId = null;
                    if (commit)
                    {
                        var unit = _context.PartUnits.First(u => u.SerialNo == row.SerialNo);
                        var actualPart = _context.Parts.Find(unit.PartId) ?? (partsByNo.TryGetValue(row.PartNo, out var p) ? p : null);
                        var actualPartNo = actualPart?.PartNo ?? row.PartNo;
                        var targetPartId = actualPart?.Id ?? unit.PartId;

                        var ratStock = ratWh != null
                            ? (_context.PartStocks.Local.FirstOrDefault(s => s.LocationId == ratWh.Id && s.PartId == targetPartId)
                               ?? _context.PartStocks.FirstOrDefault(s => s.LocationId == ratWh.Id && s.PartId == targetPartId))
                            : null;
                        var ratRepairStock = ratStock?.RepairQty ?? 0;

                        var mainStock = mainWh != null
                            ? (_context.PartStocks.Local.FirstOrDefault(s => s.LocationId == mainWh.Id && s.PartId == targetPartId)
                               ?? _context.PartStocks.FirstOrDefault(s => s.LocationId == mainWh.Id && s.PartId == targetPartId))
                            : null;
                        var mainRepairStock = mainStock?.RepairQty ?? 0;

                        var ratDeduct = Math.Min(ratRepairStock, row.Qty);
                        if (ratDeduct > 0 && ratWh != null)
                        {
                            _stock.AdjustStock(actualPartNo, ratWh.Id, -ratDeduct, "Repair", "RepairComplete", "DailyReportRow", null, userName, $"ซ่อมเสร็จจากศูนย์ซ่อม D1 Room Repair (คลังราษฎร์บูรณะ) SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unit.Id);
                        }
                        else
                        {
                            var repairDeduct = Math.Min(mainRepairStock, row.Qty);
                            if (repairDeduct > 0 && mainWh != null)
                            {
                                _stock.AdjustStock(actualPartNo, mainWh.Id, -repairDeduct, "Repair", "RepairComplete", "DailyReportRow", null, userName, $"ซ่อมเสร็จจากศูนย์ซ่อม SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unit.Id);
                            }
                        }
                        _stock.AdjustStock(actualPartNo, mainWh?.Id ?? 0, row.Qty, "Good", "RepairComplete", "DailyReportRow", null, userName, $"ซ่อมเสร็จจากศูนย์ซ่อม SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unit.Id);
                        unit.Status = "InStock";
                        unit.Condition = "Good";
                        unit.LocationId = mainWh?.Id;
                        _context.SaveChanges();
                        unitId = unit.Id;
                    }

                    results.Add(new RowResult
                    {
                        RowIndex = row.RowIndex,
                        SourceSheet = row.SourceSheet,
                        PartNo = row.PartNo,
                        PartName = row.PartName,
                        SerialNo = row.SerialNo,
                        Qty = row.Qty,
                        DhlStatus = row.Status,
                        Problem = row.Problem,
                        CaseNo = row.CaseNo,
                        FeName = row.FeName,
                        MatchType = "RepairCompleted",
                        PartUnitId = unitId,
                        Note = "ซ่อมเสร็จจากศูนย์ซ่อม — ย้ายจากสต็อกซ่อมเข้าสต็อกดี"
                    });
                }
                else if (currentUnitStatus == "InStock")
                {
                    summary.InboundRepairedCount++;
                    results.Add(new RowResult
                    {
                        RowIndex = row.RowIndex,
                        SourceSheet = row.SourceSheet,
                        PartNo = row.PartNo,
                        PartName = row.PartName,
                        SerialNo = row.SerialNo,
                        Qty = row.Qty,
                        DhlStatus = row.Status,
                        Problem = row.Problem,
                        CaseNo = row.CaseNo,
                        FeName = row.FeName,
                        MatchType = "InboundRepaired",
                        Note = "SN นี้อยู่ในสต็อกดีอยู่แล้ว — ไม่ปรับสต็อกซ้ำ"
                    });
                }
                else
                {
                    summary.InboundRepairedCount++;
                    int? unitId = null;
                    if (!string.IsNullOrWhiteSpace(row.SerialNo))
                        serialStatusOverlay[row.SerialNo] = "InStock";

                    if (commit && partsByNo.TryGetValue(row.PartNo, out var part))
                    {
                        _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, "Good", "GoodsReceipt", "DailyReportRow", null, userName, $"รับเข้าอะไหล่ซ่อมเสร็จจาก {row.ShippedFrom ?? "ศูนย์ซ่อม"} SN {row.SerialNo}", serialNo: row.SerialNo);

                        if (!string.IsNullOrWhiteSpace(row.SerialNo))
                        {
                            var unit = _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo);
                            if (unit == null)
                            {
                                unit = new PartUnit { SerialNo = row.SerialNo, PartId = part.Id };
                                _context.PartUnits.Add(unit);
                            }
                            unit.Status = "InStock";
                            unit.Condition = "Good";
                            unit.LocationId = mainWh?.Id;
                            _context.SaveChanges();
                            unitId = unit.Id;
                        }
                    }

                    results.Add(new RowResult
                    {
                        RowIndex = row.RowIndex,
                        SourceSheet = row.SourceSheet,
                        PartNo = row.PartNo,
                        PartName = row.PartName,
                        SerialNo = row.SerialNo,
                        Qty = row.Qty,
                        DhlStatus = row.Status,
                        Problem = row.Problem,
                        CaseNo = row.CaseNo,
                        FeName = row.FeName,
                        MatchType = "InboundRepaired",
                        PartUnitId = unitId,
                        StockCredited = true,
                        Note = $"รับเข้าอะไหล่ซ่อมเสร็จจาก {row.ShippedFrom ?? "ศูนย์ซ่อม"} เข้าสต็อกดี"
                    });
                }
                continue;
            }

            // ── SHEET BRANCH: Return inbound (Prioritize Ticket Return Confirmation) ──
            // Priority 1 — return confirmation against an open return line OR matching active withdraw batch by CaseNo
            var matchedByReturnCaseNo = !string.IsNullOrWhiteSpace(row.CaseNo);
            var retLine = matchedByReturnCaseNo
                ? openReturnLines.FirstOrDefault(l => (l.PartNo == row.PartNo || l.OriginalPartNo == row.PartNo) && l.Ticket!.ExternalTicketNo == row.CaseNo && remainingReturn.GetValueOrDefault(l.TicketPartLineId) > 0)
                : openReturnLines.FirstOrDefault(l => (l.PartNo == row.PartNo || l.OriginalPartNo == row.PartNo) && remainingReturn.GetValueOrDefault(l.TicketPartLineId) > 0);

            // Fallback: If no pre-opened Return line, check if there is an active Ticket / WithdrawBatch for this CaseNo & PartNo
            TicketPartLine? fallbackWithdrawLine = null;
            if (retLine == null && matchedByReturnCaseNo)
            {
                fallbackWithdrawLine = _context.TicketPartLines
                    .Include(l => l.Ticket)
                    .Include(l => l.WithdrawBatch)
                    .FirstOrDefault(l => l.Ticket!.ExternalTicketNo == row.CaseNo && (l.PartNo == row.PartNo || l.OriginalPartNo == row.PartNo) && l.LineType == "Withdraw"
                        && l.WithdrawBatch != null && l.WithdrawBatch.ReturnStatus != "คืน" && l.WithdrawBatch.Status != "Cancel");
            }

            if (retLine != null || fallbackWithdrawLine != null)
            {
                int? targetBatchId = retLine != null ? retLine.WithdrawBatchId : fallbackWithdrawLine!.WithdrawBatchId;
                int? targetTicketId = retLine != null ? retLine.TicketId : fallbackWithdrawLine!.TicketId;
                string? targetExternalTicketNo = retLine != null ? retLine.Ticket?.ExternalTicketNo : fallbackWithdrawLine!.Ticket?.ExternalTicketNo;

                if (retLine != null)
                {
                    remainingReturn[retLine.TicketPartLineId] = Math.Max(0, remainingReturn[retLine.TicketPartLineId] - row.Qty);
                }

                summary.ReturnConfirmed++;
                if (!string.IsNullOrWhiteSpace(row.SerialNo))
                    serialStatusOverlay[row.SerialNo] = row.Status == "GOOD" ? "InStock" : "InRepair";

                int? unitId = null;
                if (commit)
                {
                    var currentTechStock = (techLoc != null && partsByNo.TryGetValue(row.PartNo, out var pTech))
                        ? (_context.PartStocks.FirstOrDefault(s => s.LocationId == techLoc.Id && s.PartId == pTech.Id)?.GoodQty ?? 0)
                        : 0;
                    var techDeduct = Math.Min(currentTechStock, row.Qty);
                    if (techDeduct > 0 && techLoc != null)
                    {
                        _stock.AdjustStock(row.PartNo, techLoc.Id, -techDeduct, "Good", "Return", "WithdrawBatch", targetBatchId?.ToString(), userName, $"คืนผ่าน Daily Report SN {row.SerialNo}", serialNo: row.SerialNo);
                    }
                    _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, row.Status == "GOOD" ? "Good" : "Repair", "Return", "WithdrawBatch", targetBatchId?.ToString(), userName, $"คืนผ่าน Daily Report SN {row.SerialNo}", serialNo: row.SerialNo);

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

                    if (retLine != null)
                    {
                        retLine.ConfirmedQty += row.Qty;
                        var allLinesConfirmed = _context.TicketPartLines
                            .Where(l => l.WithdrawBatchId == retLine.WithdrawBatchId && l.LineType == "Return")
                            .All(l => l.TicketPartLineId == retLine.TicketPartLineId ? retLine.ConfirmedQty >= l.Quantity : l.ConfirmedQty >= l.Quantity);
                        if (allLinesConfirmed && retLine.WithdrawBatch != null)
                        {
                            retLine.WithdrawBatch.ReturnStatus = "คืน";
                            retLine.WithdrawBatch.UpdatedAt = DateTime.Now;
                        }
                    }
                    else if (fallbackWithdrawLine != null)
                    {
                        // Add Return line to complete the ticket lifecycle
                        var returnLine = new TicketPartLine
                        {
                            TicketId = fallbackWithdrawLine.TicketId,
                            WithdrawBatchId = fallbackWithdrawLine.WithdrawBatchId,
                            PartId = fallbackWithdrawLine.PartId,
                            PartNo = row.PartNo,
                            Quantity = row.Qty,
                            ConfirmedQty = row.Qty,
                            LineType = "Return",
                            SerialNo = row.SerialNo
                        };
                        _context.TicketPartLines.Add(returnLine);

                        if (fallbackWithdrawLine.WithdrawBatch != null)
                        {
                            fallbackWithdrawLine.WithdrawBatch.Status = "เบิก";
                            fallbackWithdrawLine.WithdrawBatch.ReturnStatus = "คืน";
                            fallbackWithdrawLine.WithdrawBatch.ReturnRequestedAt = row.TxDate ?? DateTime.Now;
                            fallbackWithdrawLine.WithdrawBatch.UpdatedAt = DateTime.Now;
                        }
                    }
                    _context.SaveChanges();
                }

                var matchNote = row.Status == "GOOD" ? "คืนสำเร็จ — เข้าสต็อกดี" : "คืนสำเร็จ — เข้าสถานะกำลังซ่อม";
                matchNote += retLine != null
                    ? (matchedByReturnCaseNo ? " (จับคู่ตรงด้วย Case No.)" : " (จับคู่แบบเดาด้วย Part No. — ไฟล์นี้ไม่มี Case No.)")
                    : $" (จับคู่ตรงด้วย Case No. #{row.CaseNo} — ปิดใบเบิกอัตโนมัติ)";

                results.Add(new RowResult
                {
                    RowIndex = row.RowIndex,
                    SourceSheet = row.SourceSheet,
                    PartNo = row.PartNo,
                    PartName = row.PartName,
                    SerialNo = row.SerialNo,
                    Qty = row.Qty,
                    DhlStatus = row.Status,
                    Problem = row.Problem,
                    MatchType = "ReturnConfirmed",
                    CaseNo = row.CaseNo,
                    FeName = row.FeName,
                    TicketId = targetTicketId,
                    WithdrawBatchId = targetBatchId,
                    ExternalTicketNo = targetExternalTicketNo,
                    PartUnitId = unitId,
                    Note = matchNote
                });
                continue;
            }

            // Priority 2 — repair completing / still in repair (for repair center loops when no ticket matches)
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
                        var currentRepairStock = _context.PartStocks.FirstOrDefault(s => s.LocationId == (mainWh != null ? mainWh.Id : 0) && s.PartId == unit.PartId)?.RepairQty ?? 0;
                        var repairDeduct = Math.Min(currentRepairStock, row.Qty);
                        if (repairDeduct > 0)
                        {
                            _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, -repairDeduct, "Repair", "RepairComplete", "DailyReportRow", null, userName, $"ซ่อมเสร็จ (Daily Report) SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unit.Id);
                        }
                        _stock.AdjustStock(row.PartNo, mainWh?.Id ?? 0, row.Qty, "Good", "RepairComplete", "DailyReportRow", null, userName, $"ซ่อมเสร็จ (Daily Report) SN {row.SerialNo}", serialNo: row.SerialNo, partUnitId: unit.Id);
                        unit.Status = "InStock";
                        unit.Condition = "Good";
                        _context.SaveChanges();
                        unitId = unit.Id;
                    }
                    results.Add(new RowResult { RowIndex = row.RowIndex, SourceSheet = row.SourceSheet, PartNo = row.PartNo, PartName = row.PartName, SerialNo = row.SerialNo, Qty = row.Qty, DhlStatus = row.Status, Problem = row.Problem, CaseNo = row.CaseNo, FeName = row.FeName, MatchType = "RepairCompleted", PartUnitId = unitId, Note = "ซ่อมเสร็จ กลับเข้าสต็อกดี" });
                }
                else
                {
                    summary.StillInRepair++;
                    results.Add(new RowResult { RowIndex = row.RowIndex, SourceSheet = row.SourceSheet, PartNo = row.PartNo, PartName = row.PartName, SerialNo = row.SerialNo, Qty = row.Qty, DhlStatus = row.Status, Problem = row.Problem, CaseNo = row.CaseNo, FeName = row.FeName, MatchType = "StillInRepair", Note = "รับเข้าสต็อกซ่อม (เคสย้อนหลัง — อยู่ระหว่างการซ่อม)" });
                }
                continue;
            }

            // Rule 3 — no open return line or active withdraw batch matches this row.
            summary.Unmatched++;
            var partExists = partsByNo.TryGetValue(row.PartNo, out var unmatchedPart);
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
                        "Return", "DailyReportRow", null, userName, $"รับคืนเข้าสต็อกคลังกลาง (เคสย้อนหลัง) SN {row.SerialNo}", serialNo: row.SerialNo);

                    if (!string.IsNullOrWhiteSpace(row.SerialNo))
                    {
                        var unit = _context.PartUnits.FirstOrDefault(u => u.SerialNo == row.SerialNo);
                        if (unit == null)
                        {
                            unit = new PartUnit { SerialNo = row.SerialNo, PartId = unmatchedPart!.Id };
                            _context.PartUnits.Add(unit);
                        }
                        unit.Status = row.Status == "GOOD" ? "InStock" : "InRepair";
                        unit.Condition = row.Status == "GOOD" ? "Good" : "Bad";
                        unit.LocationId = mainWh?.Id;
                        _context.SaveChanges();
                        unmatchedUnitId = unit.Id;
                    }
                }
            }

            var unmatchedNote = matchedByReturnCaseNo
                ? $"รับคืนของเคส #{row.CaseNo} (เคสย้อนหลัง)"
                : "รับคืนอะไหล่นอกรอบ (เคสย้อนหลัง)";
            unmatchedNote += !partExists
                ? " — ไม่พบ Part No. นี้ในระบบ จึงเพิ่มสต็อกให้ไม่ได้"
                : alreadyTracked
                    ? " — SN นี้เคยถูกบันทึกไว้แล้ว ไม่เพิ่มสต็อกซ้ำ"
                    : (row.Status == "GOOD" ? " — ปรับเข้าสต็อกดีที่คลังกลางเรียบร้อย" : " — ปรับเข้าสต็อกซ่อมที่คลังกลางเรียบร้อย");
            results.Add(new RowResult
            {
                RowIndex = row.RowIndex,
                SourceSheet = row.SourceSheet,
                PartNo = row.PartNo,
                PartName = row.PartName,
                SerialNo = row.SerialNo,
                Qty = row.Qty,
                DhlStatus = row.Status,
                Problem = row.Problem,
                MatchType = "Unmatched",
                CaseNo = row.CaseNo,
                FeName = row.FeName,
                PartUnitId = unmatchedUnitId,
                Note = unmatchedNote,
                StockCredited = shouldCredit
            });
        }

        // ── Minimum Stock Reconciliation Processing ──
        if (reconciliation.Count > 0)
        {
            foreach (var item in reconciliation)
            {
                var part = partsByNo.GetValueOrDefault(item.PartNo);
                var stock = part != null && stocksByPartId.TryGetValue(part.Id, out var s) ? s : null;
                item.SystemGoodQty = stock?.GoodQty ?? 0;
                item.SystemRepairQty = stock?.RepairQty ?? 0;

                item.DiffGood = item.SystemGoodQty - item.DhlGoodQty;
                item.DiffRepair = item.SystemRepairQty - item.DhlBadQty;

                if (part == null)
                {
                    item.Status = "NOT_IN_SYSTEM";
                    summary.ReconcileDiffCount++;
                }
                else if (item.DiffGood == 0 && item.DiffRepair == 0)
                {
                    item.Status = "MATCH";
                    summary.ReconcileMatchCount++;
                }
                else
                {
                    if (commit && syncReconcile && part != null)
                    {
                        if (stock == null)
                        {
                            stock = new PartStock { PartId = part.Id, LocationId = mainWh?.Id ?? 1, GoodQty = 0, BadQty = 0, RepairQty = 0 };
                            _context.PartStocks.Add(stock);
                            stocksByPartId[part.Id] = stock;
                        }

                        var deltaGood = item.DhlGoodQty - item.SystemGoodQty;
                        var deltaRepair = item.DhlBadQty - item.SystemRepairQty;

                        if (deltaGood != 0 && mainWh != null)
                        {
                            _stock.AdjustStock(part.PartNo, mainWh.Id, deltaGood, "Good", "StockCountAdjust", "DailyReportReconcile", null, userName, "ปรับยอดสต็อกตรงตาม Minimum Stock (Batch Import)");
                        }
                        if (deltaRepair != 0 && mainWh != null)
                        {
                            _stock.AdjustStock(part.PartNo, mainWh.Id, deltaRepair, "Repair", "StockCountAdjust", "DailyReportReconcile", null, userName, "ปรับยอดสต็อกตรงตาม Minimum Stock (Batch Import)");
                        }

                        item.SystemGoodQty = item.DhlGoodQty;
                        item.SystemRepairQty = item.DhlBadQty;
                        item.DiffGood = 0;
                        item.DiffRepair = 0;
                        item.Status = "MATCH";
                        summary.ReconcileMatchCount++;
                    }
                    else
                    {
                        item.Status = "DIFF";
                        summary.ReconcileDiffCount++;
                    }
                }

                if (commit && part != null)
                {
                    if (part.MinStock != item.DhlMinQty)
                    {
                        part.MinStock = item.DhlMinQty;
                    }
                }
            }
            if (commit)
            {
                _context.SaveChanges();
            }
        }

        return (results, summary);
    }

    private string CurrentUser() =>
        User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
        ?? User?.Identity?.Name
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? "system";
}
