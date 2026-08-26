using Microsoft.AspNetCore.Mvc;
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

    // Derived, not stored — see dbFinal3.xlsx Ticket sheet remarks: Status carries dual meaning
    // depending on which leg is active, so the leg itself is inferred from Address/lines instead
    // of a stored column.
    private static string Phase(string? returnAddress, IEnumerable<TicketPartLine> lines) =>
        returnAddress != null || lines.Any(l => l.LineType == "Return") ? "return" : "withdraw";

    // GET /api/Ticket
    [HttpGet]
    public IActionResult GetAllTickets()
    {
        var tickets = _context.Tickets
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        var lines = _context.TicketPartLines
            .Where(l => tickets.Select(t => t.TicketId).Contains(l.TicketId))
            .ToList();
        var attachments = _context.TicketAttachments
            .Where(a => tickets.Select(t => t.TicketId).Contains(a.TicketId))
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

        // One Aservice Ticket can carry multiple independent ใบเบิก (see CreateAdditionalWithdraw)
        // — number them ใบที่ 1..N in submission order so Admin/tech can tell them apart when the
        // same ExternalTicketNo shows up on more than one row.
        var siblingGroups = tickets
            .GroupBy(t => t.ExternalTicketNo)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.CreatedAt).Select(t => t.TicketId).ToList());

        var result = tickets.Select(t =>
        {
            var tLines = lines.Where(l => l.TicketId == t.TicketId).ToList();
            var siblings = siblingGroups[t.ExternalTicketNo];
            return new
            {
                t.TicketId, t.ExternalTicketNo, t.TechEmail, t.TechName, t.TechDept,
                t.Status, t.RejectReason, t.ApproverName, t.ApprovedAt,
                t.WithdrawAddress, t.ReturnAddress, t.WithdrawDescription, t.CreatedAt, t.UpdatedAt,
                t.WithdrawSlipNo, t.WithdrawDate, t.EmployeeCode, t.UsageStatus, t.TechSupportName,
                phase = Phase(t.ReturnAddress, tLines),
                siblingIndex = siblings.IndexOf(t.TicketId) + 1,
                siblingCount = siblings.Count,
                lines = tLines.Select(l => new
                {
                    l.TicketPartLineId, l.PartId, l.PartNo, l.Quantity, l.LineType, l.Condition,
                    partName = partMap.GetValueOrDefault(l.PartId, l.PartNo),
                    availableStock = l.LineType == "Withdraw" ? stockByPartId.GetValueOrDefault(l.PartId, 0) : (int?)null,
                    l.OriginalPartNo,
                    originalPartName = l.OriginalPartNo == null ? null : partNameByNo.GetValueOrDefault(l.OriginalPartNo, l.OriginalPartNo)
                }),
                attachments = attachments.Where(a => a.TicketId == t.TicketId).Select(a => new
                {
                    a.TicketAttachmentId, a.Phase, a.FilePath, a.FileName, a.UploadedAt
                })
            };
        });

        return Ok(result);
    }

    // DELETE /api/Ticket/{id} — cleanup for the self-service "New Request" flow, where a Ticket
    // is sync'd first and the withdraw is submitted right after in the same client action. If the
    // withdraw step fails (network error, validation, stale cache, etc.) the sync'd Ticket is
    // otherwise orphaned forever with Status=null and no lines. Scoped tight — only deletes a
    // Ticket that was never actually submitted — so it's safe to call from anywhere.
    [HttpDelete("{id}")]
    public IActionResult DeleteOrphanTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != null) return BadRequest(new { message = "Only a never-submitted ticket can be deleted." });
        if (_context.TicketPartLines.Any(l => l.TicketId == id)) return BadRequest(new { message = "Only a never-submitted ticket can be deleted." });

        _context.TicketAttachments.RemoveRange(_context.TicketAttachments.Where(a => a.TicketId == id));
        _context.Tickets.Remove(ticket);
        _context.SaveChanges();
        return Ok(new { message = "Deleted." });
    }

    // POST /api/Ticket/sync — upsert a ticket header from Aservice (dedupe by ExternalTicketNo).
    // Stands in for the real Aservice webhook/poll integration.
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

    // POST /api/Ticket/additional-withdraw — tech requests another ใบเบิก (withdraw slip) under
    // an Aservice Ticket number that's already been synced (e.g. the first ใบเบิก is still open,
    // but the job needs more parts). Unlike /sync this deliberately does NOT dedupe — it always
    // creates a new Ticket row with its own independent withdraw/return cycle, sharing only the
    // ExternalTicketNo. See AppDbContext: ExternalTicketNo is intentionally non-unique for this.
    [HttpPost("additional-withdraw")]
    public IActionResult CreateAdditionalWithdraw([FromBody] SyncTicketDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ExternalTicketNo))
            return BadRequest(new { message = "External Ticket No. is required." });
        if (!_context.Tickets.Any(t => t.ExternalTicketNo == dto.ExternalTicketNo))
            return BadRequest(new { message = "ไม่พบ Ticket นี้ในระบบ — sync ใบแรกก่อนถึงจะขอเบิกเพิ่มได้" });

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
        _audit.Log(User, "Ticket", ticket.TicketId.ToString(), "ADDITIONAL_WITHDRAW", null, new { ticket.TicketId, ticket.ExternalTicketNo });
        return Ok(new { message = "สร้างใบเบิกใหม่แล้ว", ticket });
    }

    // PUT /api/Ticket/{id}/withdraw — technician submits the withdraw request (Form 1).
    // Also handles resubmission after Admin Reject: a rejected ticket's old Withdraw lines and
    // RejectReason are cleared and replaced with what the tech submits this time.
    [HttpPut("{id}/withdraw")]
    public IActionResult SubmitWithdraw(int id, [FromBody] SubmitLinesDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != null && ticket.Status != "Reject")
            return BadRequest(new { message = "Only a new ticket, or one Admin rejected, can submit a withdraw request." });
        if (dto.Lines == null || dto.Lines.Count == 0) return BadRequest(new { message = "Select at least one part." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        if (ticket.Status == "Reject")
        {
            var oldLines = _context.TicketPartLines.Where(l => l.TicketId == id && l.LineType == "Withdraw");
            _context.TicketPartLines.RemoveRange(oldLines);
            ticket.RejectReason = null;
        }

        foreach (var l in dto.Lines)
        {
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == l.PartNo && p.IsActive);
            if (part == null) return BadRequest(new { message = $"Part {l.PartNo} not found." });
            _context.TicketPartLines.Add(new TicketPartLine
            {
                TicketId = id, PartId = part.Id, PartNo = l.PartNo, Quantity = l.Quantity, LineType = "Withdraw"
            });
        }

        foreach (var a in dto.Attachments ?? new())
        {
            _context.TicketAttachments.Add(new TicketAttachment
            {
                TicketId = id, Phase = "Withdraw", FilePath = a.FilePath, FileName = a.FileName
            });
        }

        ticket.WithdrawAddress = dto.Address;
        ticket.WithdrawDescription = dto.Description;
        ticket.WithdrawSlipNo ??= GenerateWithdrawSlipNo(); // never renumber a resubmit-after-reject
        ticket.WithdrawDate = dto.WithdrawDate ?? DateTime.Now;
        ticket.EmployeeCode = dto.EmployeeCode;
        ticket.UsageStatus = dto.UsageStatus;
        ticket.TechSupportName = string.IsNullOrWhiteSpace(dto.TechSupportName) ? null : dto.TechSupportName;
        ticket.UpdatedAt = DateTime.Now;
        // Persist the lines/attachments first — TryAutoApprove re-reads them from the DB, so it
        // needs to see this submission's own rows, not just whatever existed before it.
        _context.SaveChanges();

        TryAutoApprove(ticket);
        _context.SaveChanges();
        return Ok(new { message = "Withdraw request submitted.", ticket });
    }

    // "WD-{year}-{5-digit running no.}" — resets every calendar year. Counts existing slip
    // numbers for the current year rather than a dedicated sequence table; fine at this scale
    // (small internal tool, low concurrent-submit volume).
    private string GenerateWithdrawSlipNo()
    {
        var year = DateTime.Now.Year;
        var prefix = $"WD-{year}-";
        var count = _context.Tickets.Count(t => t.WithdrawSlipNo != null && t.WithdrawSlipNo.StartsWith(prefix));
        return $"{prefix}{(count + 1):D5}";
    }

    // PUT /api/Ticket/{id}/lines/{lineId}/substitute — Admin swaps a requested part for a
    // registered equivalent (e.g. requested part is out of stock) before approving.
    [HttpPut("{id}/lines/{lineId}/substitute")]
    public IActionResult SubstitutePart(int id, int lineId, [FromBody] SubstituteDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "รอ" && ticket.Status != "รออะไหล่")
            return BadRequest(new { message = "Only a waiting ticket's parts can be substituted." });

        var line = _context.TicketPartLines.FirstOrDefault(l => l.TicketPartLineId == lineId && l.TicketId == id);
        if (line == null) return NotFound(new { message = "Line not found." });
        if (line.LineType != "Withdraw") return BadRequest(new { message = "Only withdraw lines can be substituted." });

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
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "SUBSTITUTE_PART", null, new { ticket.TicketId, lineId, from = originalPartNo, to = dto.PartNo });

        // Re-run the stock check immediately — a substitution is exactly the kind of thing that
        // can turn a "รออะไหล่" ticket into one the warehouse can now fulfill, and Admin shouldn't
        // have to separately click Approve to find that out.
        TryAutoApprove(ticket);
        _context.SaveChanges();
        return Ok(new { message = "Substituted.", ticket, line });
    }

    // Auto-approve engine — runs right after a withdraw request is submitted, and again after
    // Admin substitutes an equivalent part while a ticket sits at "รออะไหล่". Mirrors what the
    // manual ApproveTicket endpoint used to do (stock deducted right here, not at ReceiveTicket),
    // just without a human clicking a button: the only condition is "does the central warehouse
    // have enough of everything on this ticket's Withdraw lines" — Tech Support being set or not
    // plays no part in this decision.
    //
    // Sets ticket.Status to "เดินทาง" (stock cut, same as a manual approve) when everything's in
    // stock, or "รออะไหล่" (nothing touched) when it isn't — never leaves it at "รอ".
    private void TryAutoApprove(Ticket ticket)
    {
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var withdrawLines = _context.TicketPartLines.Where(l => l.TicketId == ticket.TicketId && l.LineType == "Withdraw").ToList();

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
            ticket.Status = "รออะไหล่";
            ticket.UpdatedAt = DateTime.Now;
            return;
        }

        foreach (var line in withdrawLines)
        {
            _stock.AdjustStock(
                partNo: line.PartNo, locationId: mainWh!.Id, qtyDelta: -line.Quantity, condition: "Good",
                movementType: "Approve", refType: "Ticket", refId: ticket.TicketId.ToString(),
                userName: "Auto", remarks: $"Auto-approved for ticket {ticket.ExternalTicketNo}");
        }

        ticket.Status = "เดินทาง";
        ticket.ApproverName = "Auto";
        ticket.ApprovedAt = DateTime.Now;
        ticket.UpdatedAt = DateTime.Now;
        _audit.Log(User, "Ticket", ticket.TicketId.ToString(), "AUTO_APPROVE", null, new { ticket.TicketId, ticket.Status });
    }

    // PUT /api/Ticket/{id}/approve — Admin approves the withdraw request → เดินทาง
    [HttpPut("{id}/approve")]
    public IActionResult ApproveTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "รอ" && ticket.Status != "รออะไหล่")
            return BadRequest(new { message = "Only a waiting ticket can be approved." });

        // Manual fallback for a "รออะไหล่" ticket whose stock recovered without a substitution
        // (e.g. a Goods Receipt came in) — Admin can force a recheck instead of waiting for
        // another SubstitutePart call to trigger it. Everything else (a plain "รอ" ticket that
        // somehow wasn't already auto-approved at submit time) goes through the same engine.
        if (ticket.Status == "รออะไหล่")
        {
            TryAutoApprove(ticket);
            _context.SaveChanges();
            return ticket.Status == "เดินทาง"
                ? Ok(new { message = "Approved.", ticket })
                : Ok(new { message = "Still not enough stock.", ticket });
        }

        // Stock is deducted from the central warehouse right here, not at ReceiveTicket — once
        // Admin approves, that quantity is committed to this ticket and can't be double-approved
        // into another request. It does NOT land in OL-TECH yet (the part is still in transit);
        // ReceiveTicket only adds it there once the tech actually has it in hand. If the ticket is
        // Cancelled while เดินทาง (approved but not yet received), CancelTicket puts it back.
        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        var withdrawLines = _context.TicketPartLines.Where(l => l.TicketId == id && l.LineType == "Withdraw").ToList();
        foreach (var line in withdrawLines)
        {
            try
            {
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: -line.Quantity, condition: "Good",
                    movementType: "Approve", refType: "Ticket", refId: id.ToString(),
                    userName: User?.Identity?.Name ?? "admin", remarks: $"Approved for ticket {ticket.ExternalTicketNo}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        ticket.Status = "เดินทาง";
        ticket.ApproverName = User?.Identity?.Name ?? "admin";
        ticket.ApprovedAt = DateTime.Now;
        ticket.UpdatedAt = DateTime.Now;

        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "APPROVE", null, new { ticket.TicketId, ticket.Status });
        return Ok(new { message = "Approved.", ticket });
    }

    // PUT /api/Ticket/{id}/reject — Admin rejects, tech must edit and resubmit. Reason required.
    [HttpPut("{id}/reject")]
    public IActionResult RejectTicket(int id, [FromBody] RejectDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "รอ" && ticket.Status != "รออะไหล่")
            return BadRequest(new { message = "Only a waiting ticket can be rejected." });
        if (string.IsNullOrWhiteSpace(dto.Reason)) return BadRequest(new { message = "Reject reason is required." });

        ticket.Status = "Reject";
        ticket.RejectReason = dto.Reason;
        ticket.UpdatedAt = DateTime.Now;

        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "REJECT", null, new { ticket.TicketId, ticket.Status, dto.Reason });
        return Ok(new { message = "Rejected.", ticket });
    }

    // PUT /api/Ticket/{id}/cancel — Admin cancels the ticket outright. No reason needed.
    [HttpPut("{id}/cancel")]
    public IActionResult CancelTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status is "Reject" or "Cancel" or "คืน")
            return BadRequest(new { message = "Ticket is already closed." });

        // ApproveTicket deducts from WH-RAT the moment it approves (see there), so a ticket
        // cancelled while เดินทาง (approved but not yet physically received) has stock sitting
        // in limbo — put it back. Tickets cancelled at รอ never had stock touched, nothing to undo.
        if (ticket.Status == "เดินทาง")
        {
            var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
            var withdrawLines = _context.TicketPartLines.Where(l => l.TicketId == id && l.LineType == "Withdraw").ToList();
            foreach (var line in withdrawLines)
            {
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: line.Quantity, condition: "Good",
                    movementType: "Cancel", refType: "Ticket", refId: id.ToString(),
                    userName: User?.Identity?.Name ?? "admin", remarks: $"Cancelled ticket {ticket.ExternalTicketNo} — returned to warehouse");
            }
        }

        ticket.Status = "Cancel";
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        _audit.Log(User, "Ticket", id.ToString(), "CANCEL", null, new { ticket.TicketId, ticket.Status });
        return Ok(new { message = "Cancelled.", ticket });
    }

    // PUT /api/Ticket/{id}/receive — technician confirms physical receipt → เบิก (closes withdraw leg)
    [HttpPut("{id}/receive")]
    public IActionResult ReceiveTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "เดินทาง") return BadRequest(new { message = "Only an in-transit ticket can be received." });

        var withdrawLines = _context.TicketPartLines.Where(l => l.TicketId == id && l.LineType == "Withdraw").ToList();
        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");

        // The WH-RAT side already left the books at ApproveTicket — this only lands the part in
        // the tech's on-hand bucket now that they actually have it in hand. Pure addition, so
        // there's no negative-stock case to guard against here.
        foreach (var line in withdrawLines)
        {
            _stock.AdjustStock(
                partNo: line.PartNo, locationId: techLoc?.Id ?? 0, qtyDelta: line.Quantity, condition: "Good",
                movementType: "Issue", refType: "Ticket", refId: id.ToString(),
                userName: ticket.TechName, remarks: $"Issued for ticket {ticket.ExternalTicketNo}");
        }

        ticket.Status = "เบิก";
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Received.", ticket });
    }

    // PUT /api/Ticket/{id}/return — technician submits the return request (Form 2, partial return OK)
    [HttpPut("{id}/return")]
    public IActionResult SubmitReturn(int id, [FromBody] SubmitLinesDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "เบิก") return BadRequest(new { message = "Only a withdrawn ticket can be returned." });
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
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Return request submitted.", ticket });
    }

    // PUT /api/Ticket/{id}/approve-return — Admin reviews a submitted return request (parts,
    // conditions, attached photos) and confirms it before the tech is allowed to ship it out.
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

    // PUT /api/Ticket/{id}/ship — technician marks the return parcel as shipped → เดินทาง.
    // Only reachable after Admin has approved the return (อนุมัติคืน) — see ApproveReturn above.
    [HttpPut("{id}/ship")]
    public IActionResult MarkShipped(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "อนุมัติคืน" || ticket.ReturnAddress == null)
            return BadRequest(new { message = "Only an Admin-approved return can be marked as shipped." });

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
                // warehouse, Lost is a write-off, but in every case it's no longer sitting with
                // the tech once this confirms. (Previously this never touched techLoc at all, so
                // "อยู่กับช่าง" only ever grew and never reflected completed returns.)
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

    // POST /api/Ticket/upload — technician attaches a photo to a withdraw/return submission.
    // Called once per file from the frontend; the returned path is then included in the
    // Attachments list sent to /withdraw or /return so it gets tied to the ticket.
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
    // Withdraw only — see Ticket.WithdrawDate/EmployeeCode/UsageStatus.
    public DateTime? WithdrawDate { get; set; }
    public string? EmployeeCode { get; set; }
    public string? UsageStatus { get; set; } // "Repair" | "Keep"
    // Withdraw only — see Ticket.TechSupportName. Null/blank = "ไม่มี" (didn't consult anyone).
    public string? TechSupportName { get; set; }
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
