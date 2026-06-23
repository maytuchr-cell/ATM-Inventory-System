using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportController : ControllerBase
{
    private readonly AppDbContext _context;

    public ReportController(AppDbContext context) => _context = context;

    // GET /api/Report/audit-checklist?from=&to=&type= — FR-RP-01
    // Flat ledger of every stock-affecting event (GR, Issue, Return, Transfer, Disposal, Adjustment)
    [HttpGet("audit-checklist")]
    public IActionResult AuditChecklist(DateTime? from, DateTime? to, string? type)
    {
        var query = _context.StockMovements.AsQueryable();
        if (from.HasValue) query = query.Where(m => m.Timestamp >= from);
        if (to.HasValue)   query = query.Where(m => m.Timestamp <= to);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.MovementType == type);

        var movements = query.OrderByDescending(m => m.Timestamp).ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);
        var locMap  = _context.Locations.ToDictionary(l => l.Id, l => l.Name);

        var result = movements.Select(m => new
        {
            m.Id, m.MovementType, m.PartNo, m.Qty, m.Condition, m.RefType, m.RefId,
            m.Cost, m.Remarks, m.UserName, m.Timestamp,
            partName     = partMap.GetValueOrDefault(m.PartNo, m.PartNo),
            fromLocation = m.FromLocationId.HasValue ? locMap.GetValueOrDefault(m.FromLocationId.Value, "—") : null,
            toLocation   = m.ToLocationId.HasValue   ? locMap.GetValueOrDefault(m.ToLocationId.Value, "—")   : null,
        });

        return Ok(result);
    }

    // GET /api/Report/lifecycle — FR-RP-02
    // Per-part lifetime summary: total received/issued/returned/transferred/disposed + reconciliation
    [HttpGet("lifecycle")]
    public IActionResult Lifecycle()
    {
        var movements = _context.StockMovements.ToList();
        var parts = _context.Parts.ToList();
        var stockByPart = _context.PartStocks
            .GroupBy(s => s.PartId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.GoodQty) + g.Sum(s => s.DefectiveQty));

        var result = parts.Select(p =>
        {
            var partMovements = movements.Where(m => m.PartNo == p.PartNo).ToList();

            var received  = partMovements.Where(m => m.MovementType == "GR").Sum(m => m.Qty);
            var issued    = partMovements.Where(m => m.MovementType == "Issue").Sum(m => m.Qty);
            var returned  = partMovements.Where(m => m.MovementType == "Return").Sum(m => m.Qty);
            var transferred = partMovements.Where(m => m.MovementType == "Transfer" && m.ToLocationId.HasValue).Sum(m => m.Qty);
            // Only the final write-off counts as "disposed" — the auto-flag move to Scrap (RefType=AutoFlag)
            // relocates stock but doesn't destroy it yet.
            var disposed  = partMovements.Where(m => m.MovementType == "Disposal" && m.RefType == "DisposalRequest").Sum(m => m.Qty);
            var adjustments = partMovements.Where(m => m.MovementType == "Adjustment")
                .Sum(m => m.ToLocationId.HasValue ? m.Qty : -m.Qty);

            var expectedTotal = received + returned + adjustments - issued - disposed;
            var actualTotal   = stockByPart.GetValueOrDefault(p.Id, 0);

            return new
            {
                p.PartNo, p.PartName,
                received, issued, returned, transferred, disposed, adjustments,
                expectedTotal, actualTotal,
                reconciled = expectedTotal == actualTotal
            };
        }).ToList();

        return Ok(result);
    }
}
