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

    public TicketController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // GET /api/Ticket
    [HttpGet]
    public IActionResult GetAllTickets()
    {
        var tickets = _context.Tickets
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        // Enrich with part names
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var result = tickets.Select(t => new
        {
            t.TicketId, t.TechEmail, t.TechId, t.TechName, t.TechPhone, t.TechDept,
            t.RequestedPartNo, t.ApprovedPartNo,
            t.FaultySerialNo, t.FaultyPartNo, t.MachineModel, t.Description, t.AttachmentPath,
            t.Status, t.IsDOA, t.CreatedAt, t.ReceivedAt, t.DueDate,
            requestedPartNav = t.RequestedPartNo != null && partMap.ContainsKey(t.RequestedPartNo)
                ? new { partName = partMap[t.RequestedPartNo] } : null,
            approvedPartNav  = t.ApprovedPartNo  != null && partMap.ContainsKey(t.ApprovedPartNo)
                ? new { partName = partMap[t.ApprovedPartNo]  } : null,
        });

        return Ok(result);
    }

    // POST /api/Ticket
    [HttpPost]
    public IActionResult CreateTicket([FromBody] CreateTicketDto dto)
    {
        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.RequestedPartNo && p.IsActive);
        if (part == null)
            return BadRequest(new { message = "Part not found." });

        // ATM Model compatibility check: if model has assigned parts, requested part must be in that list
        if (!string.IsNullOrWhiteSpace(dto.MachineModel))
        {
            var atmModel = _context.AtmModels
                .FirstOrDefault(m => m.ModelCode == dto.MachineModel && m.IsActive);
            if (atmModel != null)
            {
                var assignedParts = _context.AtmModelParts
                    .Where(p => p.AtmModelId == atmModel.Id)
                    .Select(p => p.PartNo)
                    .ToList();
                if (assignedParts.Any() && !assignedParts.Contains(dto.RequestedPartNo!))
                    return BadRequest(new { message = $"Part '{part.PartName}' is not compatible with {atmModel.ModelName}." });
            }
        }

        // FR-SC-02: while the system is frozen for an annual stock count, new tickets are
        // accepted but held as Draft (no stock impact) until the freeze is lifted.
        var settings = _context.SystemSettings.FirstOrDefault();
        var isFrozen = settings?.IsFrozen ?? false;

        var ticket = new Ticket
        {
            TechEmail       = dto.TechEmail  ?? string.Empty,
            TechId          = dto.TechId     ?? string.Empty,
            TechName        = dto.TechName   ?? string.Empty,
            TechPhone       = dto.TechPhone  ?? string.Empty,
            TechDept        = dto.TechDept   ?? string.Empty,
            RequestedPartNo = dto.RequestedPartNo,
            FaultySerialNo  = dto.FaultySerialNo,
            FaultyPartNo    = dto.FaultyPartNo,
            MachineModel    = dto.MachineModel,
            Description     = dto.Description,
            AttachmentPath  = dto.AttachmentPath,
            Status          = isFrozen ? "Draft" : "Pending",
            CreatedAt       = DateTime.Now
        };

        _context.Tickets.Add(ticket);
        _context.SaveChanges();
        return Ok(new { message = "Ticket created.", ticket });
    }

    // PUT /api/Ticket/{id}/approve
    [HttpPut("{id}/approve")]
    public IActionResult ApproveTicket(int id, [FromBody] ApproveDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });

        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.ApprovedPartNo && p.IsActive);
        if (part == null || part.StockQuantity <= 0)
            return BadRequest(new { message = "Part unavailable or out of stock." });

        // ATM Model compatibility check on approval too
        if (!string.IsNullOrWhiteSpace(ticket.MachineModel))
        {
            var atmModel = _context.AtmModels
                .FirstOrDefault(m => m.ModelCode == ticket.MachineModel && m.IsActive);
            if (atmModel != null)
            {
                var assignedParts = _context.AtmModelParts
                    .Where(p => p.AtmModelId == atmModel.Id)
                    .Select(p => p.PartNo)
                    .ToList();
                if (assignedParts.Any() && !assignedParts.Contains(dto.ApprovedPartNo))
                    return BadRequest(new { message = $"Part '{part.PartName}' is not compatible with {atmModel.ModelName}. Cannot approve." });
            }
        }

        // FR-DP-01: expired or unrepairable parts must not be issued
        if (part.IsUnrepairable || (part.ExpiryDate.HasValue && part.ExpiryDate < DateTime.Now))
            return BadRequest(new { message = $"{part.PartName} is flagged as expired/unrepairable and cannot be issued." });

        // FR1-06: warn if technician already holds ≥ 25 parts (buffer limit)
        const int TECH_BUFFER_LIMIT = 25;
        var returnedTicketIds = _context.ReturnRequests.Select(r => r.TicketId).ToHashSet();
        int techOnHand = _context.Tickets.Count(t =>
            t.TechEmail == ticket.TechEmail &&
            (t.Status == "Approved" || t.Status == "Received") &&
            !returnedTicketIds.Contains(t.TicketId));

        if (techOnHand >= TECH_BUFFER_LIMIT && !dto.ForceApprove)
            return StatusCode(409, new
            {
                message = $"{ticket.TechName} currently holds {techOnHand} parts (limit {TECH_BUFFER_LIMIT}). Set forceApprove=true to override.",
                bufferWarning = true,
                onHandCount = techOnHand
            });

        var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT");
        if (mainWh == null) return BadRequest(new { message = "Main warehouse location not configured." });

        try
        {
            _stock.AdjustStock(
                partNo: dto.ApprovedPartNo, locationId: mainWh.Id, qtyDelta: -1, condition: "Good",
                movementType: "Issue", refType: "Ticket", refId: id.ToString(),
                userName: ticket.TechName, remarks: $"Issued for ticket TK-{id:0000}");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        ticket.ApprovedPartNo = dto.ApprovedPartNo;
        ticket.Status         = "Approved";

        _context.SaveChanges();
        return Ok(new { message = "Approved.", ticket });
    }

    // PUT /api/Ticket/{id}/receive
    [HttpPut("{id}/receive")]
    public IActionResult ReceiveTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });

        ticket.Status     = "Received";
        ticket.ReceivedAt = DateTime.Now;
        ticket.DueDate    = DateTime.Now.AddDays(5);

        _context.SaveChanges();
        return Ok(new { message = "Received. 5-day SLA started.", ticket });
    }

    // PUT /api/Ticket/{id}/doa — FR-MC-04: technician reports the issued part as Dead on Arrival
    [HttpPut("{id}/doa")]
    public IActionResult ReportDoa(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.ApprovedPartNo == null) return BadRequest(new { message = "Ticket has no issued part to report." });
        if (ticket.IsDOA) return BadRequest(new { message = "Already reported as DOA." });

        var techLoc = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN");
        if (techLoc != null)
        {
            _stock.AdjustStock(
                partNo: ticket.ApprovedPartNo, locationId: techLoc.Id, qtyDelta: 1, condition: "Defective",
                movementType: "Adjustment", refType: "Ticket", refId: id.ToString(),
                userName: ticket.TechName, remarks: $"Reported DOA on ticket TK-{id:0000}");
        }

        ticket.IsDOA = true;
        _context.SaveChanges();
        return Ok(new { message = "Reported as DOA.", ticket });
    }

    // PUT /api/Ticket/{id}/reject
    [HttpPut("{id}/reject")]
    public IActionResult RejectTicket(int id)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == id);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });

        ticket.Status = "Rejected";
        _context.SaveChanges();
        return Ok(new { message = "Rejected.", ticket });
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

public class CreateTicketDto
{
    public string? TechEmail       { get; set; }
    public string? TechId          { get; set; }
    public string? TechName        { get; set; }
    public string? TechPhone       { get; set; }
    public string? TechDept        { get; set; }
    public string? RequestedPartNo { get; set; }
    public string? FaultySerialNo  { get; set; }
    public string? FaultyPartNo    { get; set; }
    public string? MachineModel    { get; set; }
    public string? Description     { get; set; }
    public string? AttachmentPath  { get; set; }
}

public class ApproveDto
{
    public string ApprovedPartNo { get; set; } = string.Empty;
    public bool ForceApprove { get; set; } = false;
}
