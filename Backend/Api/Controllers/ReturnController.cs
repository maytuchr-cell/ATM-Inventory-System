using Microsoft.AspNetCore.Mvc;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReturnController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;

    public ReturnController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // GET /api/Return
    [HttpGet]
    public IActionResult GetAll()
    {
        var returns  = _context.ReturnRequests.OrderByDescending(r => r.CreatedAt).ToList();
        var tickets  = _context.Tickets.ToDictionary(t => t.TicketId, t => t);
        var partMap  = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);
        var locMap   = _context.Locations.ToDictionary(l => l.Id, l => l.Name);

        var result = returns.Select(r => new
        {
            r.Id, r.TicketId, r.PartNo, r.Condition, r.SourceType, r.ReturnedBy, r.CreatedAt,
            partName       = partMap.GetValueOrDefault(r.PartNo, r.PartNo),
            locationFrom   = locMap.GetValueOrDefault(r.LocationFromId, "—"),
            locationTo     = locMap.GetValueOrDefault(r.LocationToId, "—"),
            techName       = tickets.TryGetValue(r.TicketId, out var tk) ? tk.TechName : null,
            ticketRef      = $"TK-{r.TicketId:0000}"
        });

        return Ok(result);
    }

    // GET /api/Return/on-hand — tickets received by techs that have NOT yet been returned (FR-RT-03)
    [HttpGet("on-hand")]
    public IActionResult GetOnHand()
    {
        var returnedTicketIds = _context.ReturnRequests.Select(r => r.TicketId).ToHashSet();

        var onHand = _context.Tickets
            .Where(t => t.Status == "Received" && !returnedTicketIds.Contains(t.TicketId))
            .OrderBy(t => t.ReceivedAt)
            .ToList();

        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var result = onHand.Select(t => new
        {
            t.TicketId, t.TechName, t.TechDept, t.TechPhone,
            t.ApprovedPartNo, t.ReceivedAt, t.DueDate,
            partName  = t.ApprovedPartNo != null ? partMap.GetValueOrDefault(t.ApprovedPartNo, t.ApprovedPartNo) : null,
            isOverdue = t.DueDate.HasValue && DateTime.Now > t.DueDate.Value
        });

        return Ok(result);
    }

    // POST /api/Return
    [HttpPost]
    public IActionResult Create([FromBody] ReturnCreateDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == dto.TicketId);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "Received")
            return BadRequest(new { message = "Only parts from a Received ticket can be returned." });

        if (_context.ReturnRequests.Any(r => r.TicketId == dto.TicketId))
            return BadRequest(new { message = "This ticket has already been returned." });

        // FR-RT-02 rule #1: PartNo must match the ticket's approved part...
        bool isMatch = dto.PartNo == ticket.ApprovedPartNo;
        // ...rule #2 exception: parts in the same EquivalentGroup (FR1-02) are interchangeable
        bool isEquivalent = false;
        if (!isMatch && ticket.ApprovedPartNo != null)
        {
            // Legacy pairwise check
            isEquivalent = _context.EquivalentParts.Any(e =>
                e.OriginalPartNo == ticket.ApprovedPartNo && e.EquivalentPartNo == dto.PartNo);

            // Group-based check (FR1-02)
            if (!isEquivalent)
            {
                var groupIdsWithApproved = _context.EquivalentGroupMembers
                    .Where(m => m.PartNo == ticket.ApprovedPartNo)
                    .Select(m => m.GroupId)
                    .ToHashSet();

                isEquivalent = groupIdsWithApproved.Any() &&
                    _context.EquivalentGroupMembers.Any(m =>
                        groupIdsWithApproved.Contains(m.GroupId) && m.PartNo == dto.PartNo);
            }
        }

        if (!isMatch && !isEquivalent)
            return BadRequest(new { message = $"Part {dto.PartNo} does not match the part issued on this ticket and is not a registered equivalent." });

        var toLocation = _context.Locations.FirstOrDefault(l => l.Id == dto.LocationToId);
        if (toLocation == null) return BadRequest(new { message = "Target location not found." });

        var fromLocation = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN")
            ?? _context.Locations.First();

        var ret = new ReturnRequest
        {
            TicketId       = dto.TicketId,
            PartNo         = dto.PartNo,
            Condition      = dto.Condition,
            SourceType     = dto.SourceType,
            LocationFromId = fromLocation.Id,
            LocationToId   = dto.LocationToId,
            ReturnedBy     = dto.ReturnedBy ?? ticket.TechName,
            CreatedAt      = DateTime.Now
        };

        _context.ReturnRequests.Add(ret);
        _context.SaveChanges();

        _stock.AdjustStock(
            partNo: dto.PartNo, locationId: dto.LocationToId, qtyDelta: 1, condition: dto.Condition,
            movementType: "Return", refType: "Ticket", refId: dto.TicketId.ToString(),
            userName: ret.ReturnedBy, remarks: $"Returned from {dto.SourceType} for ticket TK-{dto.TicketId:0000}");
        _context.SaveChanges();

        return Ok(new { message = "Return recorded.", returnId = ret.Id });
    }
}

public class ReturnCreateDto
{
    public int TicketId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good"; // Good | Defective
    public string SourceType { get; set; } = "Technician"; // Technician | GRG | LocalVendor
    public int LocationToId { get; set; }
    public string? ReturnedBy { get; set; }
}
