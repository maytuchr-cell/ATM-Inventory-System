using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;
using Api.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class TicketController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;
    private readonly AuditService _audit;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public TicketController(AppDbContext context, StockService stock, AuditService audit, IConfiguration config, IWebHostEnvironment env)
    {
        _context = context;
        _stock = stock;
        _audit = audit;
        _config = config;
        _env = env;
    }

    // "How far along" ranking used to pick a single displayStatus for a Ticket row that may have
    // several WithdrawBatches (and/or a return leg) at different stages — the list view shows
    // whichever is furthest along. Reject/Cancel intentionally rank lowest so any batch/leg still
    // actually in progress outranks a closed-out one; a Ticket where EVERY batch is Reject/Cancel
    // and no return is in flight naturally falls back to showing one of those.
    private static readonly Dictionary<string, int> WithdrawRank = new()
    {
        ["Reject"] = 0, ["Cancel"] = 0,
        ["รอ"] = 1, ["รออะไหล่"] = 2, ["รอส่งเมล DHL"] = 3, ["เดินทาง"] = 4, ["เบิก"] = 5,
    };
    private static readonly Dictionary<string, int> ReturnRank = new()
    {
        ["Reject"] = 0, ["Cancel"] = 0,
        ["รอ"] = 6, ["อนุมัติคืน"] = 7, ["กำลังเดินทางรับคืน"] = 8, ["เดินทาง"] = 9, ["คืน"] = 10,
    };

    // Return is batch-scoped now ("คืนตามใบเบิก") — each batch contributes both its own withdraw
    // Status AND its own ReturnStatus (if any) to the ranking, so a Ticket with 3 batches each
    // mid-return-at-different-stages still collapses to one sensible "how far along" badge.
    private static string? ComputeDisplayStatus(IEnumerable<WithdrawBatch> batches)
    {
        string? best = null;
        var bestRank = -1;
        foreach (var b in batches)
        {
            if (b.Status != null)
            {
                var rank = WithdrawRank.GetValueOrDefault(b.Status, 0);
                if (rank > bestRank) { bestRank = rank; best = b.Status; }
            }
            if (b.ReturnStatus != null)
            {
                var rank = ReturnRank.GetValueOrDefault(b.ReturnStatus, 0);
                if (rank > bestRank) { bestRank = rank; best = b.ReturnStatus; }
            }
        }
        return best;
    }

    // "รออะไหล่" no longer times out into an auto-reject (removed — see TryAutoApprove) — a batch
    // waits here indefinitely until Admin substitutes a part, waits for stock and rechecks, or
    // Rejects/Cancels manually. WaitingSinceAt is kept purely for the shortage report's "waiting
    // longest first" ordering, not to drive any timeout action.
    private string? FindSubstitutePartName(int partId, int neededQty, Dictionary<int, int> stockByPartId, Dictionary<int, string> partNameById)
    {
        var groupIds = _context.EquivalentGroupMembers.Where(m => m.PartId == partId).Select(m => m.GroupId).ToHashSet();
        if (groupIds.Count == 0) return null;
        var siblingIds = _context.EquivalentGroupMembers.Where(m => groupIds.Contains(m.GroupId) && m.PartId != partId)
            .Select(m => m.PartId).Distinct().ToList();
        var viableId = siblingIds.FirstOrDefault(pid => stockByPartId.GetValueOrDefault(pid, 0) >= neededQty);
        return viableId == 0 ? null : partNameById.GetValueOrDefault(viableId);
    }

    // GET /api/Ticket/shortage-report?days=30 — Admin visibility into stock shortages: which
    // batches are stuck on "รออะไหล่" right now (longest-waiting first — this no longer times out
    // into an auto-reject, see TryAutoApprove), plus historical auto-reject-by-timeout events from
    // before that mechanism was removed (trend half stays read-only over old AUTO_REJECT_TIMEOUT
    // audit entries; no new ones will appear going forward).
    [HttpGet("shortage-report")]
    public IActionResult GetShortageReport(int days = 30)
    {
        var waitingBatches = _context.WithdrawBatches
            .Include(b => b.Ticket)
            .Where(b => b.Status == "รออะไหล่")
            .OrderBy(b => b.WaitingSinceAt)
            .ToList();

        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var stockByPartId = mainWh == null
            ? new Dictionary<int, int>()
            : _context.PartStocks.Where(s => s.LocationId == mainWh.Id).ToDictionary(s => s.PartId, s => s.GoodQty);
        var batchIds = waitingBatches.Select(b => b.WithdrawBatchId).ToList();
        var lines = _context.TicketPartLines.Where(l => l.WithdrawBatchId != null && batchIds.Contains(l.WithdrawBatchId.Value)).ToList();
        var partNameById = _context.Parts.ToDictionary(p => p.Id, p => p.PartName);

        var live = waitingBatches.Select(b =>
        {
            var shortages = lines.Where(l => l.WithdrawBatchId == b.WithdrawBatchId)
                .GroupBy(l => l.PartId)
                .Select(g => new { partId = g.Key, partName = partNameById.GetValueOrDefault(g.Key, "?"), need = g.Sum(l => l.Quantity), have = stockByPartId.GetValueOrDefault(g.Key, 0) })
                .Where(x => x.have < x.need)
                .Select(x => new { x.partName, shortQty = x.need - x.have, substitutePartName = FindSubstitutePartName(x.partId, x.need, stockByPartId, partNameById) })
                .ToList();
            return new
            {
                b.WithdrawBatchId,
                b.TicketId,
                caseNo = b.Ticket?.ExternalTicketNo,
                techName = b.Ticket?.TechName,
                techDept = b.Ticket?.TechDept,
                b.WaitingSinceAt,
                hoursWaiting = b.WaitingSinceAt.HasValue ? Math.Round((DateTime.Now - b.WaitingSinceAt.Value).TotalHours, 1) : (double?)null,
                shortages
            };
        }).ToList();

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        var events = _context.AuditLogs
            .Where(a => a.EntityType == "WithdrawBatch" && a.Action == "AUTO_REJECT_TIMEOUT" && a.Timestamp >= cutoff)
            .ToList();

        var counts = new Dictionary<string, int>();
        foreach (var e in events)
        {
            if (string.IsNullOrEmpty(e.NewValues)) continue;
            string? reason = null;
            try
            {
                using var doc = JsonDocument.Parse(e.NewValues);
                if (doc.RootElement.TryGetProperty("RejectReason", out var rr)) reason = rr.GetString();
            }
            catch (JsonException) { continue; }
            if (string.IsNullOrEmpty(reason)) continue;

            var colonIdx = reason.IndexOf(':');
            var body = colonIdx >= 0 ? reason[(colonIdx + 1)..] : reason;
            foreach (var segment in body.Split(';'))
            {
                var m = Regex.Match(segment.Trim(), @"^(.*) ขาด (\d+)$");
                if (!m.Success) continue;
                var name = m.Groups[1].Value.Trim();
                counts[name] = counts.GetValueOrDefault(name, 0) + 1;
            }
        }
        var trend = counts.OrderByDescending(kv => kv.Value)
            .Select(kv => new { partName = kv.Key, timesCausedReject = kv.Value })
            .ToList();

        return Ok(new { live, trend, trendDays = days, trendEventCount = events.Count });
    }

    // GET /api/Ticket/shortage-report/export — Excel version of the live shortage list above
    // (same rows, one line per shortage so a batch with 2 short parts gets 2 rows).
    [HttpGet("shortage-report/export")]
    public IActionResult ExportShortageReport()
    {
        var waitingBatches = _context.WithdrawBatches
            .Include(b => b.Ticket)
            .Where(b => b.Status == "รออะไหล่")
            .OrderBy(b => b.WaitingSinceAt)
            .ToList();

        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var stockByPartId = mainWh == null
            ? new Dictionary<int, int>()
            : _context.PartStocks.Where(s => s.LocationId == mainWh.Id).ToDictionary(s => s.PartId, s => s.GoodQty);
        var batchIds = waitingBatches.Select(b => b.WithdrawBatchId).ToList();
        var lines = _context.TicketPartLines.Where(l => l.WithdrawBatchId != null && batchIds.Contains(l.WithdrawBatchId.Value)).ToList();
        var partNameById = _context.Parts.ToDictionary(p => p.Id, p => p.PartName);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("อะไหล่ขาด");
        var headers = new[] { "Case No.", "ช่าง", "แผนก", "อะไหล่ที่ขาด", "จำนวนที่ขาด", "อะไหล่ทดแทนที่มีสต็อก", "รอมาแล้ว (ชม.)" };
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var b in waitingBatches)
        {
            var shortages = lines.Where(l => l.WithdrawBatchId == b.WithdrawBatchId)
                .GroupBy(l => l.PartId)
                .Select(g => new { partId = g.Key, partName = partNameById.GetValueOrDefault(g.Key, "?"), need = g.Sum(l => l.Quantity), have = stockByPartId.GetValueOrDefault(g.Key, 0) })
                .Where(x => x.have < x.need)
                .ToList();
            var hoursWaiting = b.WaitingSinceAt.HasValue ? Math.Round((DateTime.Now - b.WaitingSinceAt.Value).TotalHours, 1) : (double?)null;

            if (!shortages.Any())
            {
                ws.Cell(row, 1).Value = b.Ticket?.ExternalTicketNo ?? "";
                ws.Cell(row, 2).Value = b.Ticket?.TechName ?? "";
                ws.Cell(row, 3).Value = b.Ticket?.TechDept ?? "";
                ws.Cell(row, 7).Value = hoursWaiting ?? 0;
                row++;
                continue;
            }
            foreach (var s in shortages)
            {
                ws.Cell(row, 1).Value = b.Ticket?.ExternalTicketNo ?? "";
                ws.Cell(row, 2).Value = b.Ticket?.TechName ?? "";
                ws.Cell(row, 3).Value = b.Ticket?.TechDept ?? "";
                ws.Cell(row, 4).Value = s.partName;
                ws.Cell(row, 5).Value = s.need - s.have;
                ws.Cell(row, 6).Value = FindSubstitutePartName(s.partId, s.need, stockByPartId, partNameById) ?? "";
                ws.Cell(row, 7).Value = hoursWaiting ?? 0;
                row++;
            }
        }
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        var fileName = $"Shortage-Report-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // GET /api/Ticket
    [HttpGet]
    public IActionResult GetAllTickets()
    {
        var tickets = _context.Tickets
            .Include(t => t.WithdrawBatches)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
        var ticketIds = tickets.Select(t => t.TicketId).ToList();

        var lines = _context.TicketPartLines
            .Where(l => ticketIds.Contains(l.TicketId))
            .ToList();
        var attachments = _context.TicketAttachments
            .Where(a => ticketIds.Contains(a.TicketId))
            .OrderBy(a => a.UploadedAt)
            .ToList();
        var partMap = _context.Parts.ToDictionary(p => p.Id, p => p.PartName);
        var partNameByNo = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        // Central-warehouse on-hand per part — shown next to each Withdraw line so Admin can see
        // at a glance whether there's enough stock to approve, without opening Parts Master.
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var stockByPartId = mainWh == null
            ? new Dictionary<int, int>()
            : _context.PartStocks.Where(s => s.LocationId == mainWh.Id).ToDictionary(s => s.PartId, s => s.GoodQty);

        object LineOut(TicketPartLine l, HashSet<string> withdrawnPartNos) => new
        {
            l.TicketPartLineId, l.PartId, l.PartNo, l.Quantity, l.LineType, l.Condition,
            partName = partMap.GetValueOrDefault(l.PartId, l.PartNo),
            availableStock = l.LineType == "Withdraw" ? stockByPartId.GetValueOrDefault(l.PartId, 0) : (int?)null,
            l.OriginalPartNo,
            originalPartName = l.OriginalPartNo == null ? null : partNameByNo.GetValueOrDefault(l.OriginalPartNo, l.OriginalPartNo),
            isOffTicket = l.LineType == "Return" && !withdrawnPartNos.Contains(l.PartNo)
        };

        var result = tickets.Select(t =>
        {
            var batches = t.WithdrawBatches.OrderBy(b => b.CreatedAt).ToList();

            return new
            {
                t.TicketId, t.ExternalTicketNo, t.TechEmail, t.TechName, t.TechDept,
                t.CreatedAt, t.UpdatedAt,
                displayStatus = ComputeDisplayStatus(batches),
                withdrawBatches = batches.Select(b =>
                {
                    // A Return line on THIS batch is "off-ticket" when its PartNo was never
                    // actually withdrawn on this same batch — accounting for substitution
                    // (OriginalPartNo) so a swapped-then-returned part still counts as "matches".
                    // (SubmitReturn already blocks this server-side; kept here only for display
                    // on any older data from before that validation existed.)
                    var batchWithdrawnPartNos = lines
                        .Where(l => l.WithdrawBatchId == b.WithdrawBatchId && l.LineType == "Withdraw")
                        .SelectMany(l => new[] { l.PartNo, l.OriginalPartNo })
                        .Where(p => p != null).Select(p => p!).ToHashSet();

                    return new
                    {
                        b.WithdrawBatchId, b.Status, b.RejectReason, b.ApproverName, b.ApprovedAt, b.EmailSentAt,
                        b.WithdrawAddress, b.WithdrawDescription, b.WithdrawSlipNo, b.WithdrawDate,
                        b.EmployeeCode, b.UsageStatus, b.TechSupportName, b.CreatedAt, b.UpdatedAt,
                        b.NeededByDate, b.FeId, b.Sla, b.AtmCode, b.WaitingSinceAt,
                        b.ReturnStatus, b.ReturnRejectReason, b.ReturnApproverName, b.ReturnApprovedAt,
                        b.ReturnAddress, b.ReturnEmailSentAt,
                        lines = lines.Where(l => l.WithdrawBatchId == b.WithdrawBatchId && l.LineType == "Withdraw")
                            .Select(l => LineOut(l, batchWithdrawnPartNos)),
                        attachments = attachments.Where(a => a.WithdrawBatchId == b.WithdrawBatchId && a.Phase == "Withdraw")
                            .Select(a => new { a.TicketAttachmentId, a.Phase, a.FilePath, a.FileName, a.UploadedAt }),
                        returnLines = lines.Where(l => l.WithdrawBatchId == b.WithdrawBatchId && l.LineType == "Return")
                            .Select(l => LineOut(l, batchWithdrawnPartNos)),
                        returnAttachments = attachments.Where(a => a.WithdrawBatchId == b.WithdrawBatchId && a.Phase == "Return")
                            .Select(a => new { a.TicketAttachmentId, a.Phase, a.FilePath, a.FileName, a.UploadedAt })
                    };
                })
            };
        });

        return Ok(result);
    }

    // DELETE /api/Ticket/{id} — cleanup for the self-service "New Request" flow, where a Ticket is
    // sync'd first and the withdraw batch is submitted right after in the same client action. If
    // that submission fails (network error, validation, stale cache, etc.) the sync'd Ticket is
    // otherwise orphaned forever. Scoped tight — only a Ticket with no return in progress and no
    // withdraw batches at all — so it's safe to call from anywhere.
    [HttpDelete("{id}")]
    public IActionResult DeleteOrphanTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != null) return BadRequest(new { message = "Only a ticket with no return in progress can be deleted." });
        if (_context.WithdrawBatches.Any(b => b.TicketId == id)) return BadRequest(new { message = "Only a ticket with no withdraw batches can be deleted." });

        _context.Tickets.Remove(ticket);
        _context.SaveChanges();
        return Ok(new { message = "Deleted." });
    }

    // POST /api/Ticket/sync — upsert a ticket header from Aservice (dedupe by ExternalTicketNo,
    // now the DB's own unique constraint too). Stands in for the real Aservice webhook/poll
    // integration. A Ticket created here has no withdraw batches yet — the first (or Nth) ใบเบิก
    // is added via POST /{ticketId}/withdraw-batches.
    [HttpPost("sync")]
    public IActionResult SyncFromAservice([FromBody] SyncTicketDto dto)
    {
        var existing = _context.Tickets.FirstOrDefault(t => t.ExternalTicketNo == dto.ExternalTicketNo);
        if (existing != null) return Ok(new { message = "Already synced.", ticket = existing });

        var ticket = new Ticket
        {
            ExternalTicketNo = dto.ExternalTicketNo,
            TechEmail = dto.TechEmail ?? string.Empty,
            TechName  = dto.TechName  ?? string.Empty,
            TechDept  = dto.TechDept  ?? string.Empty,
            Status    = null,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _context.Tickets.Add(ticket);
        _context.SaveChanges();
        return Ok(new { message = "Synced.", ticket });
    }

    // POST /api/Ticket/{ticketId}/withdraw-batches — technician submits a withdraw request
    // ("ใบเบิก") under an already-synced Ticket. Works identically whether this is the Ticket's
    // first batch or its Nth — "เบิกเพิ่ม" is just calling this again on the same ticketId, no
    // special-cased sibling-Ticket creation any more (see the old, now-removed
    // POST /additional-withdraw). Resubmitting a REJECTED batch is a separate endpoint below.
    [HttpPost("{ticketId}/withdraw-batches")]
    public IActionResult SubmitWithdraw(int ticketId, [FromBody] SubmitLinesDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == ticketId);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (dto.Lines == null || dto.Lines.Count == 0) return BadRequest(new { message = "Select at least one part." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        // Validate everything before touching the context, so an invalid line never leaves a
        // half-created batch behind.
        var lineParts = new List<(LineDto line, Part part)>();
        foreach (var l in dto.Lines)
        {
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == l.PartNo && p.IsActive);
            if (part == null) return BadRequest(new { message = $"Part {l.PartNo} not found." });
            lineParts.Add((l, part));
        }

        var batch = new WithdrawBatch
        {
            TicketId = ticketId,
            WithdrawAddress = dto.Address,
            WithdrawDescription = dto.Description,
            WithdrawSlipNo = GenerateWithdrawSlipNo(),
            WithdrawDate = dto.WithdrawDate ?? DateTime.Now,
            EmployeeCode = dto.EmployeeCode,
            UsageStatus = dto.UsageStatus,
            TechSupportName = string.IsNullOrWhiteSpace(dto.TechSupportName) ? null : dto.TechSupportName,
            NeededByDate = dto.NeededByDate,
            FeId = dto.FeId,
            // Sla is no longer tech-entered — Admin sets it as a dropdown at approve time
            // (see ApproveBatch), defaulting to NBD17 there. Left null until then.
            AtmCode = dto.AtmCode,
        };
        _context.WithdrawBatches.Add(batch);

        foreach (var (l, part) in lineParts)
            _context.TicketPartLines.Add(new TicketPartLine
            {
                TicketId = ticketId, WithdrawBatch = batch, PartId = part.Id, PartNo = l.PartNo, Quantity = l.Quantity, LineType = "Withdraw"
            });

        foreach (var a in dto.Attachments ?? new())
            _context.TicketAttachments.Add(new TicketAttachment
            {
                TicketId = ticketId, WithdrawBatch = batch, Phase = "Withdraw", FilePath = a.FilePath, FileName = a.FileName
            });

        // One shot — batch, lines and attachments together; EF fixes up WithdrawBatchId on the
        // lines/attachments from the WithdrawBatch nav-property assignment above.
        _context.SaveChanges();

        TryAutoApprove(batch);
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batch.WithdrawBatchId.ToString(), "SUBMIT_WITHDRAW", null, new { batch.WithdrawBatchId, ticket.ExternalTicketNo, batch.Status });
        return Ok(new { message = "Withdraw request submitted.", ticket, batch });
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/resubmit — batch-scoped counterpart of
    // the old "SubmitWithdraw on a Reject ticket clears old lines and resubmits". Old Withdraw
    // lines/attachments and the RejectReason are cleared and replaced with what the tech submits
    // this time; the batch keeps its WithdrawSlipNo (never renumbered on a resubmit).
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/resubmit")]
    public IActionResult ResubmitWithdrawBatch(int ticketId, int batchId, [FromBody] SubmitLinesDto dto)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "Reject") return BadRequest(new { message = "Only a rejected withdraw batch can be resubmitted." });
        if (dto.Lines == null || dto.Lines.Count == 0) return BadRequest(new { message = "Select at least one part." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        var lineParts = new List<(LineDto line, Part part)>();
        foreach (var l in dto.Lines)
        {
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == l.PartNo && p.IsActive);
            if (part == null) return BadRequest(new { message = $"Part {l.PartNo} not found." });
            lineParts.Add((l, part));
        }

        var oldLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batchId);
        _context.TicketPartLines.RemoveRange(oldLines);
        batch.RejectReason = null;

        foreach (var (l, part) in lineParts)
            _context.TicketPartLines.Add(new TicketPartLine
            {
                TicketId = ticketId, WithdrawBatchId = batchId, PartId = part.Id, PartNo = l.PartNo, Quantity = l.Quantity, LineType = "Withdraw"
            });
        foreach (var a in dto.Attachments ?? new())
            _context.TicketAttachments.Add(new TicketAttachment
            {
                TicketId = ticketId, WithdrawBatchId = batchId, Phase = "Withdraw", FilePath = a.FilePath, FileName = a.FileName
            });

        batch.WithdrawAddress = dto.Address;
        batch.WithdrawDescription = dto.Description;
        batch.WithdrawDate = dto.WithdrawDate ?? DateTime.Now;
        batch.EmployeeCode = dto.EmployeeCode;
        batch.UsageStatus = dto.UsageStatus;
        batch.TechSupportName = string.IsNullOrWhiteSpace(dto.TechSupportName) ? null : dto.TechSupportName;
        batch.NeededByDate = dto.NeededByDate;
        batch.FeId = dto.FeId;
        // Sla stays Admin-only (see ApproveBatch) — not touched on resubmit.
        batch.AtmCode = dto.AtmCode;
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();

        TryAutoApprove(batch);
        _context.SaveChanges();
        return Ok(new { message = "Withdraw request resubmitted.", batch });
    }

    // "WD-{year}-{5-digit running no.}" — resets every calendar year. Counts existing slip
    // numbers for the current year rather than a dedicated sequence table; fine at this scale
    // (small internal tool, low concurrent-submit volume).
    private string GenerateWithdrawSlipNo()
    {
        var year = DateTime.Now.Year;
        var prefix = $"WD-{year}-";
        var count = _context.WithdrawBatches.Count(b => b.WithdrawSlipNo != null && b.WithdrawSlipNo.StartsWith(prefix));
        return $"{prefix}{(count + 1):D5}";
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/lines/{lineId}/substitute — Admin
    // swaps a requested part for a registered equivalent (e.g. requested part is out of stock)
    // before approving.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/lines/{lineId}/substitute")]
    public IActionResult SubstitutePart(int ticketId, int batchId, int lineId, [FromBody] SubstituteDto dto)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "รอ" && batch.Status != "รออะไหล่")
            return BadRequest(new { message = "Only a waiting withdraw batch's parts can be substituted." });

        var line = _context.TicketPartLines.FirstOrDefault(l => l.TicketPartLineId == lineId && l.WithdrawBatchId == batchId);
        if (line == null) return NotFound(new { message = "Line not found." });

        var newPart = _context.Parts.FirstOrDefault(p => p.PartNo == dto.PartNo && p.IsActive);
        if (newPart == null) return BadRequest(new { message = "Part not found." });

        var groupIds = _context.EquivalentGroupMembers.Where(m => m.PartNo == line.PartNo).Select(m => m.GroupId).ToHashSet();
        var isEquivalent = groupIds.Any() && _context.EquivalentGroupMembers.Any(m => groupIds.Contains(m.GroupId) && m.PartNo == dto.PartNo);
        if (!isEquivalent)
            return BadRequest(new { message = $"{dto.PartNo} is not a registered equivalent of {line.PartNo}." });

        var originalPartNo = line.PartNo;
        line.OriginalPartNo ??= originalPartNo; // keep the true original if substituted more than once
        line.PartId = newPart.Id;
        line.PartNo = dto.PartNo;
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "SUBSTITUTE_PART", null, new { batch.WithdrawBatchId, lineId, from = originalPartNo, to = dto.PartNo });

        // Re-run the stock check immediately — a substitution is exactly the kind of thing that
        // can turn a "รออะไหล่" batch into one the warehouse can now fulfill, and Admin shouldn't
        // have to separately click Approve to find that out.
        TryAutoApprove(batch);
        _context.SaveChanges();
        return Ok(new { message = "Substituted.", batch, line });
    }

    // Stock-check engine — runs right after a withdraw batch is submitted, and again after Admin
    // substitutes an equivalent part (or resubmits a rejected batch) while it sits at "รออะไหล่".
    // Despite the name, this no longer auto-approves anything — Admin still has to click "อนุมัติ"
    // (see ApproveBatch) even when stock is sufficient. What this decides is only "does the central
    // warehouse have enough of everything on this batch's Withdraw lines right now":
    //
    // Enough → lands on "รอ", same as a fresh submit, so it shows up for Admin to approve manually.
    // Not enough → always lands on "รออะไหล่" (no auto-reject, whether or not a substitute exists —
    // see the branch below) and stays there until Admin substitutes a part, waits for stock and
    // clicks "เช็คสต็อกอีกครั้ง" (re-runs this same check via ApproveBatch), or Rejects/Cancels
    // manually.
    private void TryAutoApprove(WithdrawBatch batch)
    {
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var withdrawLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batch.WithdrawBatchId).ToList();

        var required = withdrawLines
            .GroupBy(l => l.PartId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var available = mainWh == null
            ? new Dictionary<int, int>()
            : _context.PartStocks.Where(s => s.LocationId == mainWh.Id && required.Keys.Contains(s.PartId))
                .ToDictionary(s => s.PartId, s => s.GoodQty);
        var hasEnough = mainWh != null && required.All(r => available.GetValueOrDefault(r.Key, 0) >= r.Value);

        if (!hasEnough)
        {
            // No auto-reject here (or on a timeout — see the removed CheckAndRejectTimedOutBatches)
            // regardless of whether a substitute exists for the short part(s): this always lands on
            // "รออะไหล่" and stays there indefinitely until Admin acts — either substitutes a part
            // (SubstitutePart) or waits for stock to arrive and clicks "เช็คสต็อกอีกครั้ง" (which
            // re-runs this same check via ApproveBatch), or Rejects/Cancels manually.
            batch.Status = "รออะไหล่";
            batch.WaitingSinceAt ??= DateTime.Now; // keep the original wait start across retries
            batch.UpdatedAt = DateTime.Now;
            return;
        }
        batch.WaitingSinceAt = null;
        batch.Status = "รอ";
        batch.UpdatedAt = DateTime.Now;
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/send-email — Admin confirms the DHL
    // email actually went out. This is the real "เดินทาง" (in transit) transition — stock was
    // already cut back at auto-approve, this only moves the status forward. From here the batch is
    // locked against Cancel: DHL has been contacted, so pulling back isn't a self-service undo.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/send-email")]
    public IActionResult SendEmailConfirmedBatch(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "รอส่งเมล DHL")
            return BadRequest(new { message = "Only a batch waiting to email DHL can be marked sent." });

        batch.Status = "เดินทาง";
        batch.EmailSentAt = DateTime.Now;
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "SEND_EMAIL", null, new { batch.WithdrawBatchId, batch.Status });
        return Ok(new { message = "Marked as emailed to DHL.", batch });
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/approve — Admin approves the withdraw
    // batch → เดินทาง. Also where Admin sets the DHL SLA (see ApproveBatchDto.Sla) — this used to
    // be a tech-filled field on the withdraw form; now it's Admin's call, made at the same step as
    // approval, defaulting to NBD17 client-side.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/approve")]
    public IActionResult ApproveBatch(int ticketId, int batchId, [FromBody] ApproveBatchDto? dto = null)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "รอ" && batch.Status != "รออะไหล่")
            return BadRequest(new { message = "Only a waiting batch can be approved." });

        // Manual fallback for a "รออะไหล่" batch whose stock recovered without a substitution
        // (e.g. a Goods Receipt came in) — Admin can force a recheck instead of waiting for
        // another SubstitutePart call to trigger it. This only moves it to "รอ" (ready to approve)
        // or "Reject" (no substitute) — it does not itself approve; Admin clicks "อนุมัติ" again.
        if (batch.Status == "รออะไหล่")
        {
            TryAutoApprove(batch);
            _context.SaveChanges();
            return batch.Status == "รอ"
                ? Ok(new { message = "Stock available — ready to approve.", batch })
                : (batch.Status == "Reject"
                    ? Ok(new { message = "No substitute available — rejected.", batch })
                    : Ok(new { message = "Still not enough stock.", batch }));
        }

        // Stock is deducted from the central warehouse right here, not at ReceiveBatch — once
        // Admin approves, that quantity is committed to this batch and can't be double-approved
        // into another request. It does NOT land in OL-TECH yet (the part is still in transit);
        // ReceiveBatch only adds it there once the tech actually has it in hand. If the batch is
        // cancelled while เดินทาง... wait, cancel is locked at เดินทาง — see CancelBatch.
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var withdrawLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batchId).ToList();
        foreach (var line in withdrawLines)
        {
            try
            {
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: -line.Quantity, condition: "Good",
                    movementType: "Approve", refType: "WithdrawBatch", refId: batchId.ToString(),
                    userName: User?.Identity?.Name ?? "admin", remarks: $"Approved batch {batchId}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // → "รอส่งเมล DHL", not straight to "เดินทาง" — Admin still has to confirm the DHL email
        // actually went out (SendEmailConfirmedBatch) before this counts as in transit. Stock left
        // WH-RAT the moment this ran, same as before.
        batch.Status = "รอส่งเมล DHL";
        batch.ApproverName = User?.Identity?.Name ?? "admin";
        batch.ApprovedAt = DateTime.Now;
        batch.Sla = string.IsNullOrWhiteSpace(dto?.Sla) ? "NBD17 (Cut-off 16:00)" : dto.Sla;
        batch.UpdatedAt = DateTime.Now;

        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "APPROVE", null, new { batch.WithdrawBatchId, batch.Status });
        return Ok(new { message = "Approved.", batch });
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/reject — Admin rejects, tech must edit
    // and resubmit (see ResubmitWithdrawBatch). Reason required.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/reject")]
    public IActionResult RejectBatch(int ticketId, int batchId, [FromBody] RejectDto dto)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "รอ" && batch.Status != "รออะไหล่" && batch.Status != "รอส่งเมล DHL")
            return BadRequest(new { message = "Only a waiting batch can be rejected." });
        if (string.IsNullOrWhiteSpace(dto.Reason)) return BadRequest(new { message = "Reject reason is required." });

        // ApproveBatch already deducted WH-RAT stock for a "รอส่งเมล DHL" batch — rejecting it now
        // needs to hand that back, same as CancelBatch does for the same status.
        if (batch.Status == "รอส่งเมล DHL")
        {
            var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
            var withdrawLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batchId).ToList();
            foreach (var line in withdrawLines)
            {
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: line.Quantity, condition: "Good",
                    movementType: "Reject", refType: "WithdrawBatch", refId: batchId.ToString(),
                    userName: User?.Identity?.Name ?? "admin", remarks: $"Rejected batch {batchId} — returned to warehouse");
            }
        }

        batch.Status = "Reject";
        batch.RejectReason = dto.Reason;
        batch.WaitingSinceAt = null;
        batch.UpdatedAt = DateTime.Now;

        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "REJECT", null, new { batch.WithdrawBatchId, batch.Status, dto.Reason });
        return Ok(new { message = "Rejected.", batch });
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/cancel — Admin cancels this withdraw
    // batch outright. No reason needed. (Cancelling the RETURN leg is a separate, Ticket-scoped
    // endpoint — see CancelTicket below.)
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/cancel")]
    public IActionResult CancelBatch(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status is "Reject" or "Cancel" or "เบิก")
            return BadRequest(new { message = "Batch is already closed." });

        // Once Admin has actually told DHL something (เดินทาง), it's no longer a self-service undo.
        if (batch.Status == "เดินทาง")
            return BadRequest(new { message = "ยกเลิกไม่ได้แล้ว — Admin ส่งเมลแจ้ง DHL ไปแล้ว" });

        // Stock left WH-RAT the moment ApproveBatch ran (รอส่งเมล DHL) — cancelling from there has
        // stock sitting in limbo, put it back. Cancelling at รอ/รออะไหล่ never touched stock,
        // nothing to undo.
        if (batch.Status == "รอส่งเมล DHL")
        {
            var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
            var withdrawLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batchId).ToList();
            foreach (var line in withdrawLines)
            {
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: line.Quantity, condition: "Good",
                    movementType: "Cancel", refType: "WithdrawBatch", refId: batchId.ToString(),
                    userName: User?.Identity?.Name ?? "admin", remarks: $"Cancelled batch {batchId} — returned to warehouse");
            }
        }

        batch.Status = "Cancel";
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "CANCEL", null, new { batch.WithdrawBatchId, batch.Status });
        return Ok(new { message = "Cancelled.", batch });
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/receive — technician confirms physical
    // receipt → เบิก (closes this withdraw batch, makes it eligible to source a return).
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/receive")]
    public IActionResult ReceiveBatch(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "เดินทาง") return BadRequest(new { message = "Only an in-transit batch can be received." });

        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == ticketId);
        var withdrawLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batchId).ToList();
        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");

        // The WH-RAT side already left the books at ApproveBatch — this only lands the part in the
        // tech's on-hand bucket now that they actually have it in hand. Pure addition, so there's
        // no negative-stock case to guard against here.
        foreach (var line in withdrawLines)
        {
            _stock.AdjustStock(
                partNo: line.PartNo, locationId: techLoc?.Id ?? 0, qtyDelta: line.Quantity, condition: "Good",
                movementType: "Issue", refType: "WithdrawBatch", refId: batchId.ToString(),
                userName: ticket?.TechName ?? "", remarks: $"Issued for ticket {ticket?.ExternalTicketNo}");
        }

        batch.Status = "เบิก";
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Received.", batch });
    }

    // PUT /api/Ticket/{ticketId}/withdraw-batches/{batchId}/return — technician submits a return
    // ("คืนตามใบเบิก") against ONE specific WithdrawBatch, partial return OK. Strictly scoped —
    // every returned PartNo must be one this exact batch withdrew (no "off-ticket" extras, no
    // pulling from a different batch under the same Ticket). Batches return fully independently:
    // several batches under the same Ticket can each have their own return in flight at once.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/return")]
    public IActionResult SubmitReturn(int ticketId, int batchId, [FromBody] SubmitLinesDto dto)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "เบิก") return BadRequest(new { message = "Only a received withdraw batch can be returned." });
        if (batch.ReturnStatus is not (null or "Reject"))
            return BadRequest(new { message = "This batch already has a return in progress or completed." });
        if (dto.Lines == null || dto.Lines.Count == 0) return BadRequest(new { message = "Select at least one part." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        var withdrawnQtyByPartNo = _context.TicketPartLines
            .Where(l => l.WithdrawBatchId == batchId && l.LineType == "Withdraw")
            .ToList()
            .GroupBy(l => l.PartNo)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        // Sum requested quantity per PartNo across ALL lines (a tech may split one part across
        // several Condition rows, e.g. 1 Good + 1 Bad) — checking each line in isolation would let
        // several small lines add up to more than this batch ever withdrew.
        var requestedQtyByPartNo = (dto.Lines ?? new()).GroupBy(l => l.PartNo).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var validConditions = new[] { "Good", "Bad", "Lost" };
        foreach (var l in dto.Lines)
        {
            if (string.IsNullOrWhiteSpace(l.Condition) || !validConditions.Contains(l.Condition))
                return BadRequest(new { message = $"Condition for {l.PartNo} must be Good, Bad, or Lost." });
            if (!withdrawnQtyByPartNo.TryGetValue(l.PartNo, out var withdrawnQty))
                return BadRequest(new { message = $"{l.PartNo} was not withdrawn on this ใบเบิก — คืนได้เฉพาะอะไหล่ในใบเบิกนี้เท่านั้น" });
            if (requestedQtyByPartNo.GetValueOrDefault(l.PartNo, 0) > withdrawnQty)
                return BadRequest(new { message = $"คืน {l.PartNo} ได้ไม่เกิน {withdrawnQty} ชิ้น (จำนวนที่เบิกไป)" });
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == l.PartNo);
            if (part == null) return BadRequest(new { message = $"Part {l.PartNo} not found." });
            _context.TicketPartLines.Add(new TicketPartLine
            {
                TicketId = ticketId, WithdrawBatchId = batchId, PartId = part.Id, PartNo = l.PartNo,
                Quantity = l.Quantity, LineType = "Return", Condition = l.Condition
            });
        }

        foreach (var a in dto.Attachments ?? new())
        {
            _context.TicketAttachments.Add(new TicketAttachment
            {
                TicketId = ticketId, WithdrawBatchId = batchId, Phase = "Return", FilePath = a.FilePath, FileName = a.FileName
            });
        }

        batch.ReturnStatus = "รอ";
        batch.ReturnAddress = dto.Address;
        batch.ReturnRejectReason = null; // clear any reason left over from a previous rejected return
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "SUBMIT_RETURN", null, new { batch.WithdrawBatchId, batch.ReturnStatus });
        return Ok(new { message = "Return request submitted.", batch });
    }

    // PUT .../withdraw-batches/{batchId}/approve-return — Admin reviews a submitted return request
    // (parts, conditions, attached photos) against this one batch and confirms it.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/approve-return")]
    public IActionResult ApproveReturn(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.ReturnStatus != "รอ" || batch.ReturnAddress == null)
            return BadRequest(new { message = "Only a submitted return awaiting approval can be confirmed." });

        batch.ReturnStatus = "อนุมัติคืน";
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "APPROVE_RETURN", null, new { batch.WithdrawBatchId, batch.ReturnStatus });
        return Ok(new { message = "Return approved.", batch });
    }

    // PUT .../withdraw-batches/{batchId}/reject-return — Admin sends a submitted return back for
    // the tech to fix and resubmit (mirrors RejectBatch on the withdraw leg). Clears the Return
    // lines/reason so SubmitReturn starts clean next time — the tech still has the part in hand,
    // nothing here should look like the whole withdraw got undone.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/reject-return")]
    public IActionResult RejectReturn(int ticketId, int batchId, [FromBody] RejectDto dto)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.ReturnStatus != "รอ" || batch.ReturnAddress == null)
            return BadRequest(new { message = "Only a submitted return awaiting approval can be rejected." });
        if (string.IsNullOrWhiteSpace(dto.Reason)) return BadRequest(new { message = "Reject reason is required." });

        var returnLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batchId && l.LineType == "Return");
        _context.TicketPartLines.RemoveRange(returnLines);

        batch.ReturnStatus = null;
        batch.ReturnAddress = null;
        batch.ReturnRejectReason = dto.Reason;
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "REJECT_RETURN", null, new { batch.WithdrawBatchId, batch.ReturnStatus, dto.Reason });
        return Ok(new { message = "Return rejected.", batch });
    }

    // PUT .../withdraw-batches/{batchId}/send-email-return — Admin confirms the DHL "please come
    // collect this return" email actually went out. Return-leg counterpart of SendEmailConfirmedBatch.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/send-email-return")]
    public IActionResult SendEmailConfirmedReturn(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.ReturnStatus != "อนุมัติคืน")
            return BadRequest(new { message = "Only an approved return can be marked as emailed." });

        batch.ReturnStatus = "กำลังเดินทางรับคืน";
        batch.ReturnEmailSentAt = DateTime.Now;
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "SEND_EMAIL_RETURN", null, new { batch.WithdrawBatchId, batch.ReturnStatus });
        return Ok(new { message = "Marked as emailed to DHL.", batch });
    }

    // PUT .../withdraw-batches/{batchId}/ship-return — technician marks the return parcel as
    // shipped → เดินทาง, once DHL has actually come to collect it. Only reachable after Admin has
    // confirmed the pickup email went out (กำลังเดินทางรับคืน) — see SendEmailConfirmedReturn above.
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/ship-return")]
    public IActionResult MarkShipped(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.ReturnStatus != "กำลังเดินทางรับคืน" || batch.ReturnAddress == null)
            return BadRequest(new { message = "Only a return DHL has been told to collect can be marked as shipped." });

        batch.ReturnStatus = "เดินทาง";
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Marked as shipped.", batch });
    }

    // PUT .../withdraw-batches/{batchId}/confirm-return — DHL/warehouse confirms arrival → คืน
    // (closes this batch's return leg).
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/confirm-return")]
    public IActionResult ConfirmReturnArrived(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.Include(b => b.Ticket).FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.ReturnStatus != "เดินทาง" || batch.ReturnAddress == null)
            return BadRequest(new { message = "Only an in-transit return can be confirmed." });

        var returnLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId == batchId && l.LineType == "Return").ToList();
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");
        var techName = batch.Ticket?.TechName ?? "";
        var externalTicketNo = batch.Ticket?.ExternalTicketNo ?? "";

        foreach (var line in returnLines)
        {
            try
            {
                // Comes out of the tech's on-hand bucket either way — Good/Bad go back into the
                // warehouse, Lost is a write-off, but in every case it's no longer sitting with the
                // tech once this confirms.
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: techLoc?.Id ?? 0, qtyDelta: -line.Quantity, condition: "Good",
                    movementType: "Return", refType: "WithdrawBatch", refId: batchId.ToString(),
                    userName: techName, remarks: $"Returned ({line.Condition}) for ticket {externalTicketNo}");

                // Lost means the part never actually came back — nothing to add to warehouse
                // stock, it's a write-off (covers both a genuinely missing part and a
                // non-circulating "baby part").
                if (line.Condition == "Lost") continue;

                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: line.Quantity, condition: line.Condition ?? "Good",
                    movementType: "Return", refType: "WithdrawBatch", refId: batchId.ToString(),
                    userName: techName, remarks: $"Returned ({line.Condition}) for ticket {externalTicketNo}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        batch.ReturnStatus = "คืน";
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Return confirmed.", batch });
    }

    // PUT .../withdraw-batches/{batchId}/cancel-return — Admin cancels this batch's RETURN leg in
    // progress. No reason needed. (Cancelling the WITHDRAW leg itself is CancelBatch above.)
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/cancel-return")]
    public IActionResult CancelReturn(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.ReturnStatus is null or "Reject" or "Cancel" or "คืน")
            return BadRequest(new { message = "This batch has no return in progress to cancel." });

        // Once Admin has actually told DHL something (กำลังเดินทางรับคืน, or เดินทาง which only
        // happens after that point once the tech has shipped), it's no longer a self-service undo.
        if (batch.ReturnStatus == "กำลังเดินทางรับคืน" || batch.ReturnStatus == "เดินทาง")
            return BadRequest(new { message = "ยกเลิกไม่ได้แล้ว — Admin ส่งเมลแจ้ง DHL ไปแล้ว" });

        batch.ReturnStatus = "Cancel";
        batch.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "WithdrawBatch", batchId.ToString(), "CANCEL_RETURN", null, new { batch.WithdrawBatchId, batch.ReturnStatus });
        return Ok(new { message = "Cancelled.", batch });
    }

    // POST /api/Ticket/upload — technician attaches a photo to a withdraw/return submission.
    // Called once per file from the frontend; the returned path is then included in the
    // Attachments list sent to /withdraw-batches or /return so it gets tied to the batch/ticket.
    [HttpPost("upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadAttachment(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });
        if (file.Length > 10_000_000)
            return BadRequest(new { message = "File too large (max 10MB)." });

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext))
            return BadRequest(new { message = "Only image files are allowed." });

        // Same external asset folder that part images are served from (see Program.cs AssetPath /
        // /assets static mapping) — kept outside the repo so uploads don't bloat git.
        var assetRoot = _config["AssetPath"] ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploads = Path.Combine(assetRoot, "tickets");
        Directory.CreateDirectory(uploads);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploads, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new { filePath = $"/assets/tickets/{fileName}", fileName = file.FileName });
    }

    // POST /api/Ticket/export-dhl-excel — Admin selects several withdraw batches ready to notify
    // DHL about ("รอส่งเมล DHL") and/or several returns ("อนุมัติคืน") and downloads one Excel
    // file covering all of them, instead of composing a separate email per item. Doesn't change
    // any state — purely a read/export.
    [HttpPost("export-dhl-excel")]
    public IActionResult ExportDhlExcel([FromBody] ExportDhlExcelDto dto)
    {
        var withdrawBatchIds = dto.WithdrawBatchIds ?? new();
        var returnBatchIds = dto.ReturnBatchIds ?? new();
        if (withdrawBatchIds.Count == 0 && returnBatchIds.Count == 0)
            return BadRequest(new { message = "Select at least one item to export." });

        var partNameByNo = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        using var wb = new ClosedXML.Excel.XLWorkbook();

        // Withdraw sheet — column-for-column match of DHL's own "Delivery Request Form" template,
        // so this file can be handed to DHL as-is with no reformatting on their end.
        if (withdrawBatchIds.Count > 0)
        {
            var ws = wb.Worksheets.Add("Delivery Request Form");
            var headers = new[] {
                "เลขที่ใบเบิก", "วันที่ขอเบิก", "วันที่ต้องการอะไหล่", "Part Number", "อะไหล่", "จำนวน",
                "รหัสพนักงาน", "ชื่อนามสกุล", "Case No.", "", "", "", "", "", "FE ID", "SLA", "ที่อยู่"
            };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            ws.Row(1).Style.Font.Bold = true;

            var batches = _context.WithdrawBatches.Include(b => b.Ticket).Where(b => withdrawBatchIds.Contains(b.WithdrawBatchId)).ToList();
            var batchLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId != null && withdrawBatchIds.Contains(l.WithdrawBatchId.Value)).ToList();

            // Mimics DHL's own "{Zone}/{running no.}{TechName}" convention using our own data —
            // we don't have their internal sequence number, so WithdrawSlipNo stands in for it.
            string SlipRef(WithdrawBatch b) => $"{b.Ticket?.TechDept}/{b.WithdrawSlipNo}{b.Ticket?.TechName}";
            string UsageLabel(string? usageStatus) => usageStatus switch { "Repair" => "ใช้งาน", "Keep" => "เก็บ", _ => "" };
            string SiteRef(WithdrawBatch b) => string.IsNullOrWhiteSpace(b.AtmCode) ? (b.WithdrawAddress ?? "") : $"{b.AtmCode}/{b.WithdrawAddress}";

            int row = 2;
            foreach (var b in batches)
            {
                var bLines = batchLines.Where(l => l.WithdrawBatchId == b.WithdrawBatchId).ToList();
                void WriteRowHeader()
                {
                    ws.Cell(row, 1).Value = SlipRef(b);
                    ws.Cell(row, 2).Value = b.WithdrawDate?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    ws.Cell(row, 3).Value = b.NeededByDate?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    ws.Cell(row, 7).Value = b.EmployeeCode ?? "";
                    ws.Cell(row, 8).Value = b.Ticket?.TechName ?? "";
                    ws.Cell(row, 9).Value = b.Ticket?.ExternalTicketNo ?? "";
                    ws.Cell(row, 10).Value = SiteRef(b);
                    ws.Cell(row, 11).Value = UsageLabel(b.UsageStatus);
                    ws.Cell(row, 12).Value = "อนุมัติ"; // only batches already at รอส่งเมล DHL reach this export
                    ws.Cell(row, 13).Value = b.ApproverName ?? "";
                    ws.Cell(row, 14).Value = b.ApprovedAt?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    ws.Cell(row, 15).Value = b.FeId ?? "";
                    ws.Cell(row, 16).Value = b.Sla ?? "";
                    ws.Cell(row, 17).Value = b.WithdrawAddress ?? "";
                }

                if (!bLines.Any())
                {
                    WriteRowHeader();
                    row++;
                    continue;
                }
                foreach (var l in bLines)
                {
                    WriteRowHeader();
                    ws.Cell(row, 4).Value = l.PartNo;
                    ws.Cell(row, 5).Value = partNameByNo.GetValueOrDefault(l.PartNo, l.PartNo);
                    ws.Cell(row, 6).Value = l.Quantity;
                    row++;
                }
            }
            ws.Columns().AdjustToContents();

            // DHL's own template ships these 3 reference sheets alongside the request form
            // (FE contact list, bank site contact list, part catalogue) — bundled as a static
            // snapshot so recipients have them without a separate file.
            AddCsvResourceSheet(wb, "Data.dhl_contact_fe.csv", "Contact list DataOne FE");
            AddCsvResourceSheet(wb, "Data.dhl_contact_bank.csv", "Contact list ธนาคาร");
            AddCsvResourceSheet(wb, "Data.dhl_parts.csv", "Part");
        }

        // Return sheet — separate, simpler layout (not part of DHL's inbound request form).
        // Batch-scoped now ("คืนตามใบเบิก") — each row's Case No. still comes from the batch's
        // Ticket, but the return address/lines are the BATCH's own, not shared across batches.
        if (returnBatchIds.Count > 0)
        {
            var ws = wb.Worksheets.Add("คืนอะไหล่");
            var headers = new[] { "Case No.", "ชื่อช่าง", "แผนก", "รหัสอะไหล่", "ชื่ออะไหล่", "จำนวน", "สภาพ", "ที่อยู่" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            ws.Row(1).Style.Font.Bold = true;

            var returnBatches = _context.WithdrawBatches.Include(b => b.Ticket).Where(b => returnBatchIds.Contains(b.WithdrawBatchId)).ToList();
            var rLines = _context.TicketPartLines.Where(l => l.WithdrawBatchId != null && returnBatchIds.Contains(l.WithdrawBatchId.Value) && l.LineType == "Return").ToList();
            int row = 2;
            foreach (var b in returnBatches)
            {
                var batchLines2 = rLines.Where(l => l.WithdrawBatchId == b.WithdrawBatchId).ToList();
                if (!batchLines2.Any())
                {
                    ws.Cell(row, 1).Value = b.Ticket?.ExternalTicketNo ?? "";
                    ws.Cell(row, 2).Value = b.Ticket?.TechName ?? "";
                    ws.Cell(row, 3).Value = b.Ticket?.TechDept ?? "";
                    ws.Cell(row, 8).Value = b.ReturnAddress ?? "";
                    row++;
                    continue;
                }
                foreach (var l in batchLines2)
                {
                    ws.Cell(row, 1).Value = b.Ticket?.ExternalTicketNo ?? "";
                    ws.Cell(row, 2).Value = b.Ticket?.TechName ?? "";
                    ws.Cell(row, 3).Value = b.Ticket?.TechDept ?? "";
                    ws.Cell(row, 4).Value = l.PartNo;
                    ws.Cell(row, 5).Value = partNameByNo.GetValueOrDefault(l.PartNo, l.PartNo);
                    ws.Cell(row, 6).Value = l.Quantity;
                    ws.Cell(row, 7).Value = l.Condition ?? "";
                    ws.Cell(row, 8).Value = b.ReturnAddress ?? "";
                    row++;
                }
            }
            ws.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        var fileName = $"DHL-Export-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // Reads an embedded CSV resource (see Api.csproj's EmbeddedResource entries) and writes it
    // verbatim as a new worksheet — used to bundle DHL's static reference sheets into the export.
    private static void AddCsvResourceSheet(ClosedXML.Excel.XLWorkbook wb, string resourceSuffix, string sheetName)
    {
        var assembly = typeof(TicketController).Assembly;
        var resourceName = $"{assembly.GetName().Name}.{resourceSuffix}";
        using var resStream = assembly.GetManifestResourceStream(resourceName);
        if (resStream == null) return;
        using var reader = new StreamReader(resStream, System.Text.Encoding.UTF8);

        var ws = wb.Worksheets.Add(sheetName);
        int row = 1;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cells = ParseCsvLine(line);
            for (int c = 0; c < cells.Count; c++) ws.Cell(row, c + 1).Value = cells[c];
            row++;
        }
        if (row > 1) ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { result.Add(current.ToString()); current.Clear(); }
                else current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}

public class ExportDhlExcelDto
{
    public List<int>? WithdrawBatchIds { get; set; } = new();  // withdraw-leg candidates
    public List<int>? ReturnBatchIds { get; set; } = new();    // return-leg candidates (also batch-scoped now)
}

public class SubstituteDto
{
    public string PartNo { get; set; } = string.Empty;
}

public class SyncTicketDto
{
    public string ExternalTicketNo { get; set; } = string.Empty;
    public string? TechEmail { get; set; }
    public string? TechName  { get; set; }
    public string? TechDept  { get; set; }
}

public class SubmitLinesDto
{
    public List<LineDto> Lines { get; set; } = new();
    public string Address { get; set; } = string.Empty;
    // Paths returned by POST /api/Ticket/upload for photos the tech attached to this submission.
    public List<AttachmentDto>? Attachments { get; set; }
    // Withdraw only — free-text note on why these parts are needed (e.g. "Card reader เสีย").
    public string? Description { get; set; }
    // Withdraw only — see WithdrawBatch.WithdrawDate/EmployeeCode/UsageStatus.
    public DateTime? WithdrawDate { get; set; }
    public string? EmployeeCode { get; set; }
    public string? UsageStatus { get; set; } // "Repair" | "Keep"
    // Withdraw only — see WithdrawBatch.TechSupportName. Null/blank = "ไม่มี" (didn't consult anyone).
    public string? TechSupportName { get; set; }
    // Withdraw only — DHL Delivery Request Form fields, see WithdrawBatch.cs. Sla is NOT here —
    // it's Admin-only, set at approve time (see ApproveBatch/ApproveBatchDto).
    public DateTime? NeededByDate { get; set; }
    public string? FeId { get; set; }
    public string? AtmCode { get; set; }
}

public class AttachmentDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public class LineDto
{
    public string PartNo { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Condition { get; set; } // Return lines only: Good | Bad | Lost
}

public class RejectDto
{
    public string Reason { get; set; } = string.Empty;
}

public class ApproveBatchDto
{
    // DHL delivery urgency tier — Admin's call at approve time, see ApproveBatch. Null/blank
    // defaults to NBD17 server-side too, so an old client that doesn't send this still works.
    public string? Sla { get; set; }
}
