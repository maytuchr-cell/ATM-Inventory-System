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

    // GET /api/Return/on-hand — tickets currently withdrawn (เบิก) by techs, not yet returned
    [HttpGet("on-hand")]
    public IActionResult GetOnHand()
    {
        var onHand = _context.Tickets
            .Where(t => t.Status == "เบิก")
            .OrderBy(t => t.UpdatedAt)
            .ToList();

        var lines = _context.TicketPartLines
            .Where(l => onHand.Select(t => t.TicketId).Contains(l.TicketId) && l.LineType == "Withdraw")
            .ToList();

        var result = onHand.Select(t => new
        {
            t.TicketId, t.ExternalTicketNo, t.TechName, t.TechDept,
            lines = lines.Where(l => l.TicketId == t.TicketId).Select(l => new { l.PartNo, l.Quantity })
        });

        return Ok(result);
    }

    // POST /api/Return — warehouse-side return of a part previously withdrawn on a Ticket
    [HttpPost]
    public IActionResult Create([FromBody] ReturnCreateDto dto)
    {
        var ticket = _context.Tickets.FirstOrDefault(t => t.TicketId == dto.TicketId);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (ticket.Status != "เบิก")
            return BadRequest(new { message = "Only parts from a withdrawn (เบิก) ticket can be returned." });

        // FR-RT-02 rule #1: PartNo must match one of the ticket's withdrawn parts...
        var withdrawnPartNos = _context.TicketPartLines
            .Where(l => l.TicketId == dto.TicketId && l.LineType == "Withdraw")
            .Select(l => l.PartNo)
            .ToHashSet();
        bool isMatch = withdrawnPartNos.Contains(dto.PartNo);
        // ...rule #2 exception: parts in the same EquivalentGroup (FR1-02) are interchangeable
        bool isEquivalent = false;
        if (!isMatch)
        {
            isEquivalent = _context.EquivalentParts.Any(e =>
                withdrawnPartNos.Contains(e.OriginalPartNo) && e.EquivalentPartNo == dto.PartNo);

            if (!isEquivalent)
            {
                var groupIdsWithApproved = _context.EquivalentGroupMembers
                    .Where(m => withdrawnPartNos.Contains(m.PartNo))
                    .Select(m => m.GroupId)
                    .ToHashSet();

                isEquivalent = groupIdsWithApproved.Any() &&
                    _context.EquivalentGroupMembers.Any(m =>
                        groupIdsWithApproved.Contains(m.GroupId) && m.PartNo == dto.PartNo);
            }
        }

        if (!isMatch && !isEquivalent)
            return BadRequest(new { message = $"Part {dto.PartNo} does not match a part withdrawn on this ticket and is not a registered equivalent." });

        var toLocation = _context.Locations.FirstOrDefault(l => l.Id == dto.LocationToId);
        if (toLocation == null) return BadRequest(new { message = "Target location not found." });

        var fromLocation = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN")
            ?? _context.Locations.First();

        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.PartNo);
        if (part == null) return BadRequest(new { message = $"Part {dto.PartNo} not found." });

        var ret = new ReturnRequest
        {
            TicketId       = dto.TicketId,
            PartId         = part.Id,
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
