using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;
using Api.Services;

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

    private static string? ComputeDisplayStatus(IEnumerable<WithdrawBatch> batches, string? returnStatus)
    {
        string? best = null;
        var bestRank = -1;
        foreach (var b in batches)
        {
            if (b.Status == null) continue;
            var rank = WithdrawRank.GetValueOrDefault(b.Status, 0);
            if (rank > bestRank) { bestRank = rank; best = b.Status; }
        }
        if (returnStatus != null)
        {
            var rank = ReturnRank.GetValueOrDefault(returnStatus, 0);
            if (rank > bestRank) { bestRank = rank; best = returnStatus; }
        }
        return best;
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
            var returnLines = lines.Where(l => l.TicketId == t.TicketId && l.LineType == "Return").ToList();

            // A Return line is "off-ticket" (คืนเพิ่มเติมนอกเหนือใบเบิก) when its PartNo was never
            // actually withdrawn on ANY of this Ticket's WithdrawBatches — accounting for
            // substitution (OriginalPartNo) so a swapped-then-returned part still counts as
            // "matches the withdraw".
            var withdrawnPartNos = lines
                .Where(l => l.LineType == "Withdraw" && l.WithdrawBatchId != null && batches.Select(b => b.WithdrawBatchId).Contains(l.WithdrawBatchId.Value))
                .SelectMany(l => new[] { l.PartNo, l.OriginalPartNo })
                .Where(p => p != null)
                .Select(p => p!)
                .ToHashSet();

            return new
            {
                t.TicketId, t.ExternalTicketNo, t.TechEmail, t.TechName, t.TechDept,
                t.Status, t.RejectReason, t.ApproverName, t.ApprovedAt,
                t.ReturnAddress, t.CreatedAt, t.UpdatedAt, t.ReturnEmailSentAt,
                displayStatus = ComputeDisplayStatus(batches, t.Status),
                withdrawBatches = batches.Select(b => new
                {
                    b.WithdrawBatchId, b.Status, b.RejectReason, b.ApproverName, b.ApprovedAt, b.EmailSentAt,
                    b.WithdrawAddress, b.WithdrawDescription, b.WithdrawSlipNo, b.WithdrawDate,
                    b.EmployeeCode, b.UsageStatus, b.TechSupportName, b.CreatedAt, b.UpdatedAt,
                    b.NeededByDate, b.FeId, b.Sla, b.AtmCode,
                    lines = lines.Where(l => l.WithdrawBatchId == b.WithdrawBatchId).Select(l => LineOut(l, withdrawnPartNos)),
                    attachments = attachments.Where(a => a.WithdrawBatchId == b.WithdrawBatchId)
                        .Select(a => new { a.TicketAttachmentId, a.Phase, a.FilePath, a.FileName, a.UploadedAt })
                }),
                lines = returnLines.Select(l => LineOut(l, withdrawnPartNos)),
                attachments = attachments.Where(a => a.TicketId == t.TicketId && a.Phase == "Return")
                    .Select(a => new { a.TicketAttachmentId, a.Phase, a.FilePath, a.FileName, a.UploadedAt })
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
            Sla = dto.Sla,
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
        batch.Sla = dto.Sla;
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

    // Auto-approve engine — runs right after a withdraw batch is submitted, and again after Admin
    // substitutes an equivalent part while a batch sits at "รออะไหล่". The only condition is "does
    // the central warehouse have enough of everything on this batch's Withdraw lines" — Tech
    // Support being set or not plays no part in this decision.
    //
    // Sets batch.Status to "รอส่งเมล DHL" (stock cut immediately) when everything's in stock, or
    // "รออะไหล่" (nothing touched) when it isn't — never leaves it at "รอ".
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
            batch.Status = "รออะไหล่";
            batch.UpdatedAt = DateTime.Now;
            return;
        }

        var externalTicketNo = _context.Tickets.Where(t => t.TicketId == batch.TicketId).Select(t => t.ExternalTicketNo).FirstOrDefault();
        foreach (var line in withdrawLines)
        {
            _stock.AdjustStock(
                partNo: line.PartNo, locationId: mainWh!.Id, qtyDelta: -line.Quantity, condition: "Good",
                movementType: "Approve", refType: "WithdrawBatch", refId: batch.WithdrawBatchId.ToString(),
                userName: "Auto", remarks: $"Auto-approved for ticket {externalTicketNo}");
        }

        batch.Status = "รอส่งเมล DHL";
        batch.ApproverName = "Auto";
        batch.ApprovedAt = DateTime.Now;
        batch.UpdatedAt = DateTime.Now;
        _audit.Log(User, "WithdrawBatch", batch.WithdrawBatchId.ToString(), "AUTO_APPROVE", null, new { batch.WithdrawBatchId, batch.Status });
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
    // batch → เดินทาง
    [HttpPut("{ticketId}/withdraw-batches/{batchId}/approve")]
    public IActionResult ApproveBatch(int ticketId, int batchId)
    {
        var batch = _context.WithdrawBatches.FirstOrDefault(b => b.WithdrawBatchId == batchId && b.TicketId == ticketId);
        if (batch == null) return NotFound(new { message = "Batch not found." });
        if (batch.Status != "รอ" && batch.Status != "รออะไหล่")
            return BadRequest(new { message = "Only a waiting batch can be approved." });

        // Manual fallback for a "รออะไหล่" batch whose stock recovered without a substitution
        // (e.g. a Goods Receipt came in) — Admin can force a recheck instead of waiting for
        // another SubstitutePart call to trigger it.
        if (batch.Status == "รออะไหล่")
        {
            TryAutoApprove(batch);
            _context.SaveChanges();
            return batch.Status == "รอส่งเมล DHL"
                ? Ok(new { message = "Approved.", batch })
                : Ok(new { message = "Still not enough stock.", batch });
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

        batch.Status = "เดินทาง";
        batch.ApproverName = User?.Identity?.Name ?? "admin";
        batch.ApprovedAt = DateTime.Now;
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

        // Auto-approve already deducted WH-RAT stock for a "รอส่งเมล DHL" batch — rejecting it now
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

        // Stock left WH-RAT the moment auto-approve ran (รอส่งเมล DHL) — cancelling from there has
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

    // PUT /api/Ticket/{id}/return — technician submits the return request (Form 2, partial return
    // OK). Sourced from any/all of this Ticket's withdraw batches currently at "เบิก" — no more
    // combining separate sibling Tickets client-side, since it's all one Ticket now.
    [HttpPut("{id}/return")]
    public IActionResult SubmitReturn(int id, [FromBody] SubmitLinesDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (!_context.WithdrawBatches.Any(b => b.TicketId == id && b.Status == "เบิก"))
            return BadRequest(new { message = "Only a ticket with at least one received withdraw batch can be returned." });
        if (dto.Lines == null || dto.Lines.Count == 0) return BadRequest(new { message = "Select at least one part." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        var validConditions = new[] { "Good", "Bad", "Lost" };
        foreach (var l in dto.Lines)
        {
            if (string.IsNullOrWhiteSpace(l.Condition) || !validConditions.Contains(l.Condition))
                return BadRequest(new { message = $"Condition for {l.PartNo} must be Good, Bad, or Lost." });
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == l.PartNo);
            if (part == null) return BadRequest(new { message = $"Part {l.PartNo} not found." });
            _context.TicketPartLines.Add(new TicketPartLine
            {
                TicketId = id, PartId = part.Id, PartNo = l.PartNo, Quantity = l.Quantity, LineType = "Return", Condition = l.Condition
            });
        }

        foreach (var a in dto.Attachments ?? new())
        {
            _context.TicketAttachments.Add(new TicketAttachment
            {
                TicketId = id, Phase = "Return", FilePath = a.FilePath, FileName = a.FileName
            });
        }

        ticket.Status = "รอ";
        ticket.ReturnAddress = dto.Address;
        ticket.RejectReason = null; // clear any reason left over from a previous rejected return
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Return request submitted.", ticket });
    }

    // PUT /api/Ticket/{id}/approve-return — Admin reviews a submitted return request (parts,
    // conditions, attached photos — both the lines that match an original withdraw and any extra
    // "off-ticket" ones the tech added, see isOffTicket on GetAllTickets) and confirms it.
    [HttpPut("{id}/approve-return")]
    public IActionResult ApproveReturn(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "รอ" || ticket.ReturnAddress == null)
            return BadRequest(new { message = "Only a submitted return awaiting approval can be confirmed." });

        ticket.Status = "อนุมัติคืน";
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "APPROVE_RETURN", null, new { ticket.TicketId, ticket.Status });
        return Ok(new { message = "Return approved.", ticket });
    }

    // PUT /api/Ticket/{id}/reject-return — Admin sends a submitted return back for the tech to fix
    // and resubmit (mirrors RejectBatch on the withdraw leg). Clears the Return lines/reason so
    // SubmitReturn starts clean next time, and reverts the ticket to เบิก — the tech still has the
    // part in hand, nothing here should look like the whole withdraw got undone.
    [HttpPut("{id}/reject-return")]
    public IActionResult RejectReturn(int id, [FromBody] RejectDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "รอ" || ticket.ReturnAddress == null)
            return BadRequest(new { message = "Only a submitted return awaiting approval can be rejected." });
        if (string.IsNullOrWhiteSpace(dto.Reason)) return BadRequest(new { message = "Reject reason is required." });

        var returnLines = _context.TicketPartLines.Where(l => l.TicketId == id && l.LineType == "Return");
        _context.TicketPartLines.RemoveRange(returnLines);

        ticket.Status = "เบิก";
        ticket.ReturnAddress = null;
        ticket.RejectReason = dto.Reason;
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "REJECT_RETURN", null, new { ticket.TicketId, ticket.Status, dto.Reason });
        return Ok(new { message = "Return rejected.", ticket });
    }

    // PUT /api/Ticket/{id}/send-email-return — Admin confirms the DHL "please come collect this
    // return" email actually went out. Return-leg counterpart of SendEmailConfirmedBatch. From
    // here the ticket is locked against Cancel, same reasoning as the withdraw leg.
    [HttpPut("{id}/send-email-return")]
    public IActionResult SendEmailConfirmedReturn(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "อนุมัติคืน")
            return BadRequest(new { message = "Only an approved return can be marked as emailed." });

        ticket.Status = "กำลังเดินทางรับคืน";
        ticket.ReturnEmailSentAt = DateTime.Now;
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "SEND_EMAIL_RETURN", null, new { ticket.TicketId, ticket.Status });
        return Ok(new { message = "Marked as emailed to DHL.", ticket });
    }

    // PUT /api/Ticket/{id}/ship — technician marks the return parcel as shipped → เดินทาง, once
    // DHL has actually come to collect it. Only reachable after Admin has confirmed the pickup
    // email went out (กำลังเดินทางรับคืน) — see SendEmailConfirmedReturn above.
    [HttpPut("{id}/ship")]
    public IActionResult MarkShipped(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "กำลังเดินทางรับคืน" || ticket.ReturnAddress == null)
            return BadRequest(new { message = "Only a return DHL has been told to collect can be marked as shipped." });

        ticket.Status = "เดินทาง";
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Marked as shipped.", ticket });
    }

    // PUT /api/Ticket/{id}/confirm-return — DHL/warehouse confirms arrival → คืน (closes return leg)
    [HttpPut("{id}/confirm-return")]
    public IActionResult ConfirmReturnArrived(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "เดินทาง" || ticket.ReturnAddress == null)
            return BadRequest(new { message = "Only an in-transit return can be confirmed." });

        var returnLines = _context.TicketPartLines.Where(l => l.TicketId == id && l.LineType == "Return").ToList();
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");

        foreach (var line in returnLines)
        {
            try
            {
                // Comes out of the tech's on-hand bucket either way — Good/Bad go back into the
                // warehouse, Lost is a write-off, but in every case it's no longer sitting with the
                // tech once this confirms.
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: techLoc?.Id ?? 0, qtyDelta: -line.Quantity, condition: "Good",
                    movementType: "Return", refType: "Ticket", refId: id.ToString(),
                    userName: ticket.TechName, remarks: $"Returned ({line.Condition}) for ticket {ticket.ExternalTicketNo}");

                // Lost means the part never actually came back — nothing to add to warehouse
                // stock, it's a write-off (covers both a genuinely missing part and a
                // non-circulating "baby part").
                if (line.Condition == "Lost") continue;

                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: line.Quantity, condition: line.Condition ?? "Good",
                    movementType: "Return", refType: "Ticket", refId: id.ToString(),
                    userName: ticket.TechName, remarks: $"Returned ({line.Condition}) for ticket {ticket.ExternalTicketNo}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        ticket.Status = "คืน";
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Return confirmed.", ticket });
    }

    // PUT /api/Ticket/{id}/cancel — Admin cancels the RETURN leg in progress. No reason needed.
    // (Cancelling an individual withdraw batch is CancelBatch above — a Ticket no longer has a
    // single withdraw status of its own to cancel.)
    [HttpPut("{id}/cancel")]
    public IActionResult CancelTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status is null or "Reject" or "Cancel" or "คืน")
            return BadRequest(new { message = "Ticket has no return in progress to cancel." });

        // Once Admin has actually told DHL something (กำลังเดินทางรับคืน, or เดินทาง which only
        // happens after that point once the tech has shipped), it's no longer a self-service undo.
        if (ticket.Status == "กำลังเดินทางรับคืน" || ticket.Status == "เดินทาง")
            return BadRequest(new { message = "ยกเลิกไม่ได้แล้ว — Admin ส่งเมลแจ้ง DHL ไปแล้ว" });

        ticket.Status = "Cancel";
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "CANCEL", null, new { ticket.TicketId, ticket.Status });
        return Ok(new { message = "Cancelled.", ticket });
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
        var returnTicketIds = dto.TicketIds ?? new();
        if (withdrawBatchIds.Count == 0 && returnTicketIds.Count == 0)
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
        if (returnTicketIds.Count > 0)
        {
            var ws = wb.Worksheets.Add("คืนอะไหล่");
            var headers = new[] { "Case No.", "ชื่อช่าง", "แผนก", "รหัสอะไหล่", "ชื่ออะไหล่", "จำนวน", "สภาพ", "ที่อยู่" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            ws.Row(1).Style.Font.Bold = true;

            var tickets = _context.Tickets.Where(t => returnTicketIds.Contains(t.TicketId)).ToList();
            var tLines = _context.TicketPartLines.Where(l => returnTicketIds.Contains(l.TicketId) && l.LineType == "Return").ToList();
            int row = 2;
            foreach (var t in tickets)
            {
                var ticketLines = tLines.Where(l => l.TicketId == t.TicketId).ToList();
                if (!ticketLines.Any())
                {
                    ws.Cell(row, 1).Value = t.ExternalTicketNo;
                    ws.Cell(row, 2).Value = t.TechName;
                    ws.Cell(row, 3).Value = t.TechDept;
                    ws.Cell(row, 8).Value = t.ReturnAddress ?? "";
                    row++;
                    continue;
                }
                foreach (var l in ticketLines)
                {
                    ws.Cell(row, 1).Value = t.ExternalTicketNo;
                    ws.Cell(row, 2).Value = t.TechName;
                    ws.Cell(row, 3).Value = t.TechDept;
                    ws.Cell(row, 4).Value = l.PartNo;
                    ws.Cell(row, 5).Value = partNameByNo.GetValueOrDefault(l.PartNo, l.PartNo);
                    ws.Cell(row, 6).Value = l.Quantity;
                    ws.Cell(row, 7).Value = l.Condition ?? "";
                    ws.Cell(row, 8).Value = t.ReturnAddress ?? "";
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
    public List<int> TicketIds { get; set; } = new();          // return-leg candidates (unchanged)
    public List<int>? WithdrawBatchIds { get; set; } = new();  // withdraw-leg candidates (batch-scoped)
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
    // Withdraw only — DHL Delivery Request Form fields, see WithdrawBatch.cs.
    public DateTime? NeededByDate { get; set; }
    public string? FeId { get; set; }
    public string? Sla { get; set; }
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
