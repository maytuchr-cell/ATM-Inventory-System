using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public DashboardController(AppDbContext context) => _context = context;

    // On-hand total per part (PartId → SUM(GoodQty)) from PartStock, the source of truth.
    private Dictionary<int, int> StockTotals() =>
        _context.PartStocks
            .GroupBy(s => s.PartId)
            .Select(g => new { PartId = g.Key, Good = g.Sum(x => x.GoodQty) })
            .ToDictionary(x => x.PartId, x => x.Good);

    // GET /api/Dashboard/alerts — FR-MC-01: Min/Max/Reorder breaches
    [HttpGet("alerts")]
    public IActionResult GetAlerts()
    {
        var parts = _context.Parts.Where(p => p.IsActive).ToList();
        var stockTotals = StockTotals();

        var result = parts.Select(p =>
        {
            var qty = stockTotals.GetValueOrDefault(p.Id, 0);
            return new
            {
                p.PartNo, p.PartName, StockQuantity = qty, p.MinStock, p.MaxStock, p.ReorderPoint,
                alertType = qty <= p.MinStock ? "Below Min"
                          : qty <= p.ReorderPoint ? "Reorder"
                          : qty > p.MaxStock ? "Over Max"
                          : (string?)null
            };
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
                badQty = g.Sum(s => s.BadQty),
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

        var stockTotals = StockTotals();
        var parts = _context.Parts.Where(p => p.IsActive).ToList()
            .Where(p => stockTotals.GetValueOrDefault(p.Id, 0) > 0).ToList();

        var result = parts.Select(p => new
        {
            p.PartNo, p.PartName, StockQuantity = stockTotals.GetValueOrDefault(p.Id, 0),
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
        var ticketIds = _context.Tickets
            .Where(t => t.CreatedAt >= cutoff)
            .Select(t => t.TicketId)
            .ToHashSet();
        var techByTicket = _context.Tickets
            .Where(t => ticketIds.Contains(t.TicketId))
            .ToDictionary(t => t.TicketId, t => t.TechName);

        var lines = _context.TicketPartLines
            .Where(l => ticketIds.Contains(l.TicketId) && l.LineType == "Withdraw")
            .ToList();

        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var grouped = lines
            .GroupBy(l => new { TechName = techByTicket.GetValueOrDefault(l.TicketId, "—"), l.PartNo })
            .Where(g => g.Count() > 1)
            .Select(g => new
            {
                techName = g.Key.TechName,
                partNo   = g.Key.PartNo,
                partName = partMap.GetValueOrDefault(g.Key.PartNo, g.Key.PartNo),
                count    = g.Count(),
                firstRequested = g.Min(l => l.CreatedAt),
                lastRequested  = g.Max(l => l.CreatedAt),
            })
            .OrderByDescending(x => x.count)
            .ToList();

        return Ok(grouped);
    }
}
