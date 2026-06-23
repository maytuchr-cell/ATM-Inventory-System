using Microsoft.AspNetCore.Mvc;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DisposalController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;

    public DisposalController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // GET /api/Disposal
    [HttpGet]
    public IActionResult GetAll()
    {
        var requests = _context.DisposalRequests.OrderByDescending(d => d.CreatedAt).ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);
        var locMap  = _context.Locations.ToDictionary(l => l.Id, l => l.Name);

        var result = requests.Select(d => new
        {
            d.Id, d.PartNo, d.SerialNo, d.LocationId, d.Qty, d.Status, d.ReasonCode,
            d.RequestedBy, d.ApprovedBy, d.CreatedAt, d.ApprovedAt, d.DisposedAt,
            partName     = partMap.GetValueOrDefault(d.PartNo, d.PartNo),
            locationName = locMap.GetValueOrDefault(d.LocationId, "—"),
        });

        return Ok(result);
    }

    // POST /api/Disposal/scan — FR-DP-01/02: auto-flag expired/unrepairable parts, move stock to Scrap
    [HttpPost("scan")]
    public IActionResult Scan()
    {
        var scrap = _context.Locations.FirstOrDefault(l => l.LocationType == "SCRAP");
        if (scrap == null) return BadRequest(new { message = "No Scrap location configured." });

        var now = DateTime.Now;
        var flaggedParts = _context.Parts
            .Where(p => p.IsActive && (p.IsUnrepairable || (p.ExpiryDate != null && p.ExpiryDate < now)))
            .ToList();

        var flaggedCount = 0;
        var movedCount = 0;

        foreach (var part in flaggedParts)
        {
            var stocksElsewhere = _context.PartStocks
                .Where(s => s.PartId == part.Id && s.LocationId != scrap.Id && s.GoodQty > 0)
                .ToList();

            foreach (var stock in stocksElsewhere)
            {
                var qty = stock.GoodQty;
                _stock.MoveStock(
                    partNo: part.PartNo, fromLocationId: stock.LocationId, toLocationId: scrap.Id,
                    qty: qty, condition: "Good", movementType: "Disposal", refType: "AutoFlag",
                    refId: part.PartNo, userName: "System",
                    remarks: part.IsUnrepairable ? "Auto-flagged: unrepairable" : "Auto-flagged: expired");
                movedCount++;
            }

            var hasOpenRequest = _context.DisposalRequests.Any(d => d.PartNo == part.PartNo && d.Status != "Disposed");
            if (!hasOpenRequest)
            {
                var scrapStock = _context.PartStocks.FirstOrDefault(s => s.PartId == part.Id && s.LocationId == scrap.Id);
                _context.DisposalRequests.Add(new DisposalRequest
                {
                    PartNo      = part.PartNo,
                    LocationId  = scrap.Id,
                    Qty         = scrapStock?.GoodQty ?? 0,
                    Status      = "Pending",
                    ReasonCode  = part.IsUnrepairable ? "Unrepairable" : "Expired",
                    RequestedBy = "System (auto-flag)",
                    CreatedAt   = DateTime.Now
                });
                flaggedCount++;
            }
        }

        _context.SaveChanges();
        return Ok(new { message = "Scan complete.", flaggedCount, movedCount });
    }

    // POST /api/Disposal — manual disposal request (e.g. physically damaged part found during inspection)
    [HttpPost]
    public IActionResult Create([FromBody] DisposalCreateDto dto)
    {
        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.PartNo);
        if (part == null) return BadRequest(new { message = "Part not found." });

        var location = _context.Locations.FirstOrDefault(l => l.Id == dto.LocationId);
        if (location == null) return BadRequest(new { message = "Location not found." });

        var request = new DisposalRequest
        {
            PartNo      = dto.PartNo,
            SerialNo    = dto.SerialNo,
            LocationId  = dto.LocationId,
            Qty         = dto.Qty,
            Status      = "Pending",
            ReasonCode  = dto.ReasonCode,
            RequestedBy = dto.RequestedBy ?? "Unknown",
            CreatedAt   = DateTime.Now
        };
        _context.DisposalRequests.Add(request);
        _context.SaveChanges();
        return Ok(new { message = "Disposal request created.", requestId = request.Id });
    }

    // PUT /api/Disposal/{id}/approve
    [HttpPut("{id}/approve")]
    public IActionResult Approve(int id, [FromBody] DisposalActionDto dto)
    {
        var request = _context.DisposalRequests.FirstOrDefault(d => d.Id == id);
        if (request == null) return NotFound(new { message = "Disposal request not found." });
        if (request.Status != "Pending") return BadRequest(new { message = "Only pending requests can be approved." });

        request.Status     = "Approved";
        request.ApprovedBy = dto.UserName ?? "Unknown";
        request.ApprovedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Disposal request approved.", request });
    }

    // PUT /api/Disposal/{id}/dispose — FR-DP-04: permanent stock write-off
    [HttpPut("{id}/dispose")]
    public IActionResult Dispose(int id, [FromBody] DisposalActionDto dto)
    {
        var request = _context.DisposalRequests.FirstOrDefault(d => d.Id == id);
        if (request == null) return NotFound(new { message = "Disposal request not found." });
        if (request.Status != "Approved") return BadRequest(new { message = "Only approved requests can be disposed." });

        try
        {
            _stock.AdjustStock(
                partNo: request.PartNo, locationId: request.LocationId, qtyDelta: -request.Qty,
                condition: "Good", movementType: "Disposal", refType: "DisposalRequest",
                refId: id.ToString(), userName: dto.UserName ?? "Unknown",
                remarks: $"Disposed — reason: {request.ReasonCode}");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        request.Status     = "Disposed";
        request.DisposedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Part disposed and stock written off.", request });
    }
}

public class DisposalCreateDto
{
    public string PartNo { get; set; } = string.Empty;
    public string? SerialNo { get; set; }
    public int LocationId { get; set; }
    public int Qty { get; set; } = 1;
    public string ReasonCode { get; set; } = "Other";
    public string? RequestedBy { get; set; }
}

public class DisposalActionDto
{
    public string? UserName { get; set; }
}
