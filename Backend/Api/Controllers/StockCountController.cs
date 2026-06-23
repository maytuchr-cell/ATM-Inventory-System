using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StockCountController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;

    public StockCountController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // GET /api/StockCount
    [HttpGet]
    public IActionResult GetAll()
    {
        var counts = _context.StockCounts.Include(c => c.Lines).OrderByDescending(c => c.CreatedAt).ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);
        var locMap  = _context.Locations.ToDictionary(l => l.Id, l => l.Name);

        var result = counts.Select(c => new
        {
            c.Id, c.CountType, c.Period, c.Status, c.IsSystemFrozen, c.StartedBy, c.CreatedAt, c.CompletedAt,
            lines = c.Lines.Select(l => new
            {
                l.Id, l.PartNo, l.LocationId, l.SystemQty, l.PhysicalQty, l.Variance, l.AdjustApproved, l.Remarks,
                partName     = partMap.GetValueOrDefault(l.PartNo, l.PartNo),
                locationName = locMap.GetValueOrDefault(l.LocationId, "—"),
            })
        });

        return Ok(result);
    }

    // POST /api/StockCount/start — FR-SC-01: random-selects N parts for a cycle/annual count
    [HttpPost("start")]
    public IActionResult Start([FromBody] StartCountDto dto)
    {
        if (_context.StockCounts.Any(c => c.Status != "Completed"))
            return BadRequest(new { message = "An existing stock count is still in progress." });

        var allStocks = _context.PartStocks.ToList();
        if (allStocks.Count == 0) return BadRequest(new { message = "No stock records to count." });

        var sampleSize = dto.CountType == "Annual" ? allStocks.Count : Math.Min(dto.SampleSize ?? 10, allStocks.Count);
        var sample = dto.CountType == "Annual"
            ? allStocks
            : allStocks.OrderBy(_ => Guid.NewGuid()).Take(sampleSize).ToList();

        var partMap = _context.Parts.ToDictionary(p => p.Id, p => p.PartNo);

        var count = new StockCount
        {
            CountType       = dto.CountType,
            Period          = dto.Period,
            Status          = "InProgress",
            IsSystemFrozen  = dto.CountType == "Annual", // FR-SC-02: annual counts freeze the system
            StartedBy       = dto.StartedBy ?? "Unknown",
            CreatedAt       = DateTime.Now
        };

        foreach (var s in sample)
        {
            count.Lines.Add(new StockCountLine
            {
                PartNo     = partMap.GetValueOrDefault(s.PartId, ""),
                LocationId = s.LocationId,
                SystemQty  = s.GoodQty
            });
        }

        _context.StockCounts.Add(count);

        if (count.IsSystemFrozen)
        {
            var settings = _context.SystemSettings.FirstOrDefault();
            if (settings == null)
            {
                settings = new SystemSettings { Id = 1 };
                _context.SystemSettings.Add(settings);
            }
            settings.IsFrozen = true;
            settings.ActiveStockCountId = count.Id;
        }

        _context.SaveChanges();
        return Ok(new { message = "Stock count started.", countId = count.Id, lineCount = count.Lines.Count });
    }

    // PUT /api/StockCount/{id}/lines/{lineId} — enter a physical count for one line
    [HttpPut("{id}/lines/{lineId}")]
    public IActionResult SubmitLine(int id, int lineId, [FromBody] SubmitLineDto dto)
    {
        var line = _context.StockCountLines.FirstOrDefault(l => l.Id == lineId && l.StockCountId == id);
        if (line == null) return NotFound(new { message = "Count line not found." });

        line.PhysicalQty = dto.PhysicalQty;
        _context.SaveChanges();
        return Ok(new { message = "Physical count recorded.", variance = line.Variance });
    }

    // PUT /api/StockCount/{id}/reconcile — FR-SC-03: approve + apply variance adjustments
    [HttpPut("{id}/reconcile")]
    public IActionResult Reconcile(int id, [FromBody] ReconcileDto dto)
    {
        var count = _context.StockCounts.Include(c => c.Lines).FirstOrDefault(c => c.Id == id);
        if (count == null) return NotFound(new { message = "Stock count not found." });
        if (count.Status != "InProgress") return BadRequest(new { message = "Only an in-progress count can be reconciled." });

        if (string.IsNullOrWhiteSpace(dto.Remarks))
            return BadRequest(new { message = "Remarks are required to reconcile a stock count." });

        var unsubmitted = count.Lines.Any(l => l.PhysicalQty == null);
        if (unsubmitted) return BadRequest(new { message = "All lines must have a physical count entered before reconciling." });

        foreach (var line in count.Lines.Where(l => l.Variance != 0))
        {
            _stock.AdjustStock(
                partNo: line.PartNo, locationId: line.LocationId, qtyDelta: line.Variance, condition: "Good",
                movementType: "Adjustment", refType: "StockCount", refId: id.ToString(),
                userName: dto.UserName ?? "Unknown", remarks: dto.Remarks);
            line.AdjustApproved = true;
        }

        count.Status      = "Completed";
        count.CompletedAt = DateTime.Now;

        if (count.IsSystemFrozen)
        {
            var settings = _context.SystemSettings.FirstOrDefault();
            if (settings != null)
            {
                settings.IsFrozen = false;
                settings.ActiveStockCountId = null;
            }

            // Release any Draft tickets held during the freeze back into the normal Pending queue.
            foreach (var draft in _context.Tickets.Where(t => t.Status == "Draft"))
                draft.Status = "Pending";
        }

        _context.SaveChanges();
        return Ok(new { message = "Stock count reconciled and completed." });
    }

    // GET /api/StockCount/settings — frontend checks this to show freeze banner
    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var settings = _context.SystemSettings.FirstOrDefault();
        return Ok(new { isFrozen = settings?.IsFrozen ?? false, activeStockCountId = settings?.ActiveStockCountId });
    }
}

public class StartCountDto
{
    public string CountType { get; set; } = "Cycle"; // Cycle | Annual
    public string Period { get; set; } = string.Empty;
    public int? SampleSize { get; set; }
    public string? StartedBy { get; set; }
}

public class SubmitLineDto
{
    public int PhysicalQty { get; set; }
}

public class ReconcileDto
{
    public string? Remarks { get; set; }
    public string? UserName { get; set; }
}
