using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public DashboardController(AppDbContext context) => _context = context;

    // GET /api/Dashboard/alerts — FR-MC-01: Min/Max/Reorder breaches
    [HttpGet("alerts")]
    public IActionResult GetAlerts()
    {
        var parts = _context.Parts.Where(p => p.IsActive).ToList();

        var result = parts.Select(p => new
        {
            p.PartNo, p.PartName, p.StockQuantity, p.MinStock, p.MaxStock, p.ReorderPoint,
            alertType = p.StockQuantity <= p.MinStock ? "Below Min"
                      : p.StockQuantity <= p.ReorderPoint ? "Reorder"
                      : p.StockQuantity > p.MaxStock ? "Over Max"
                      : null
        }).Where(p => p.alertType != null).ToList();

        return Ok(result);
    }

    // GET /api/Dashboard/stock — current stock grouped by Location + Condition
    [HttpGet("stock")]
    public IActionResult GetStockByLocation()
    {
        var stocks = _context.PartStocks.ToList();
        var partMap = _context.Parts.ToDictionary(p => p.Id, p => p.PartNo);
        var locMap  = _context.Locations.ToDictionary(l => l.Id, l => l.Name);

        var grouped = stocks
            .GroupBy(s => s.LocationId)
            .Select(g => new
            {
                locationId   = g.Key,
                locationName = locMap.GetValueOrDefault(g.Key, "—"),
                goodQty      = g.Sum(s => s.GoodQty),
                defectiveQty = g.Sum(s => s.DefectiveQty),
            });

        return Ok(grouped);
    }

    // GET /api/Dashboard/aging?days=30 — parts whose stock hasn't moved in N days
    [HttpGet("aging")]
    public IActionResult GetAging(int days = 30)
    {
        var cutoff = DateTime.Now.AddDays(-days);
        var lastMovementByPart = _context.StockMovements
            .GroupBy(m => m.PartNo)
            .Select(g => new { PartNo = g.Key, LastMoved = g.Max(m => m.Timestamp) })
            .ToDictionary(x => x.PartNo, x => x.LastMoved);

        var parts = _context.Parts.Where(p => p.IsActive && p.StockQuantity > 0).ToList();

        var result = parts.Select(p => new
        {
            p.PartNo, p.PartName, p.StockQuantity,
            lastMoved = lastMovementByPart.GetValueOrDefault(p.PartNo, p.CreatedAt),
            agingDays = (int)(DateTime.Now - lastMovementByPart.GetValueOrDefault(p.PartNo, p.CreatedAt)).TotalDays
        })
        .Where(p => p.lastMoved < cutoff)
        .OrderByDescending(p => p.agingDays)
        .ToList();

        return Ok(result);
    }

    // GET /api/Dashboard/top-bottom — top/bottom 20 parts by Issue movement count
    [HttpGet("top-bottom")]
    public IActionResult GetTopBottom()
    {
        var issueCounts = _context.StockMovements
            .Where(m => m.MovementType == "Issue")
            .GroupBy(m => m.PartNo)
            .Select(g => new { PartNo = g.Key, Count = g.Sum(m => m.Qty) })
            .ToList();

        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);
        var ranked = issueCounts
            .Select(x => new { x.PartNo, partName = partMap.GetValueOrDefault(x.PartNo, x.PartNo), x.Count })
            .OrderByDescending(x => x.Count)
            .ToList();

        return Ok(new
        {
            top20    = ranked.Take(20),
            bottom20 = ranked.OrderBy(x => x.Count).Take(20)
        });
    }

    // GET /api/Dashboard/recurrent-failures?days=30 — same tech + part requested more than once in window
    [HttpGet("recurrent-failures")]
    public IActionResult GetRecurrentFailures(int days = 30)
    {
        var cutoff = DateTime.Now.AddDays(-days);
        var tickets = _context.Tickets
            .Where(t => t.CreatedAt >= cutoff && t.RequestedPartNo != null)
            .ToList();

        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var grouped = tickets
            .GroupBy(t => new { t.TechId, t.TechName, t.RequestedPartNo })
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                techName = g.Key.TechName,
                partNo   = g.Key.RequestedPartNo,
                partName = g.Key.RequestedPartNo != null && partMap.TryGetValue(g.Key.RequestedPartNo, out var pn) ? pn : g.Key.RequestedPartNo,
                count    = g.Count(),
                firstRequested = g.Min(t => t.CreatedAt),
                lastRequested  = g.Max(t => t.CreatedAt),
            })
            .OrderByDescending(x => x.count)
            .ToList();

        return Ok(grouped);
    }
}
