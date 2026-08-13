using Microsoft.AspNetCore.Mvc;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TicketController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;
    private readonly AuditService _audit;

    public TicketController(AppDbContext context, StockService stock, AuditService audit)
    {
        _context = context;
        _stock = stock;
        _audit = audit;
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
        var partMap = _context.Parts.ToDictionary(p => p.Id, p => p.PartName);

        var result = tickets.Select(t =>
        {
            var tLines = lines.Where(l => l.TicketId == t.TicketId).ToList();
            return new
            {
                t.TicketId, t.ExternalTicketNo, t.TechEmail, t.TechName, t.TechDept,
                t.Status, t.RejectReason, t.ApproverName, t.ApprovedAt,
                t.WithdrawAddress, t.ReturnAddress, t.CreatedAt, t.UpdatedAt,
                phase = Phase(t.ReturnAddress, tLines),
                lines = tLines.Select(l => new
                {
                    l.TicketPartLineId, l.PartId, l.PartNo, l.Quantity, l.LineType,
                    partName = partMap.GetValueOrDefault(l.PartId, l.PartNo)
                })
            };
        });

        return Ok(result);
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

    // PUT /api/Ticket/{id}/withdraw — technician submits the withdraw request (Form 1)
    [HttpPut("{id}/withdraw")]
    public IActionResult SubmitWithdraw(int id, [FromBody] SubmitLinesDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != null) return BadRequest(new { message = "Ticket already has a withdraw request." });
        if (dto.Lines == null || dto.Lines.Count == 0) return BadRequest(new { message = "Select at least one part." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        foreach (var l in dto.Lines)
        {
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == l.PartNo && p.IsActive);
            if (part == null) return BadRequest(new { message = $"Part {l.PartNo} not found." });
            _context.TicketPartLines.Add(new TicketPartLine
            {
                TicketId = id, PartId = part.Id, PartNo = l.PartNo, Quantity = l.Quantity, LineType = "Withdraw"
            });
        }

        ticket.Status = "รอ";
        ticket.WithdrawAddress = dto.Address;
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Withdraw request submitted.", ticket });
    }

    // PUT /api/Ticket/{id}/approve — Admin approves the withdraw request → เดินทาง
    [HttpPut("{id}/approve")]
    public IActionResult ApproveTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "รอ") return BadRequest(new { message = "Only a waiting ticket can be approved." });

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
        if (ticket.Status != "รอ") return BadRequest(new { message = "Only a waiting ticket can be rejected." });
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

        foreach (var line in withdrawLines)
        {
            try
            {
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: techLoc?.Id ?? 0, qtyDelta: line.Quantity, condition: "Good",
                    movementType: "Issue", refType: "Ticket", refId: id.ToString(),
                    userName: ticket.TechName, remarks: $"Issued for ticket {ticket.ExternalTicketNo}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
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

        foreach (var l in dto.Lines)
        {
            var part = _context.Parts.FirstOrDefault(p => p.PartNo == l.PartNo);
            if (part == null) return BadRequest(new { message = $"Part {l.PartNo} not found." });
            _context.TicketPartLines.Add(new TicketPartLine
            {
                TicketId = id, PartId = part.Id, PartNo = l.PartNo, Quantity = l.Quantity, LineType = "Return"
            });
        }

        ticket.Status = "รอ";
        ticket.ReturnAddress = dto.Address;
        ticket.UpdatedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Return request submitted.", ticket });
    }

    // PUT /api/Ticket/{id}/ship — technician marks the return parcel as shipped → เดินทาง
    [HttpPut("{id}/ship")]
    public IActionResult MarkShipped(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "รอ" || ticket.ReturnAddress == null)
            return BadRequest(new { message = "Only a waiting return can be marked as shipped." });

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

        foreach (var line in returnLines)
        {
            try
            {
                _stock.AdjustStock(
                    partNo: line.PartNo, locationId: mainWh?.Id ?? 0, qtyDelta: line.Quantity, condition: "Good",
                    movementType: "Return", refType: "Ticket", refId: id.ToString(),
                    userName: ticket.TechName, remarks: $"Returned for ticket {ticket.ExternalTicketNo}");
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

    // POST /api/Ticket/upload
    [HttpPost("upload")]
    public async Task<IActionResult> UploadAttachment(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext))
            return BadRequest(new { message = "Only image files are allowed." });

        var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        Directory.CreateDirectory(uploads);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploads, fileName);

        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return Ok(new { path = $"/uploads/{fileName}" });
    }
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
}

public class LineDto
{
    public string PartNo { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class RejectDto
{
    public string Reason { get; set; } = string.Empty;
}
