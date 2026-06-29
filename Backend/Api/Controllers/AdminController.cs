using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    public AdminController(AppDbContext context) => _context = context;

    // GET /api/Admin/integrity
    // Data-integrity health check: finds rows that reference a non-existent Part (orphans),
    // and reconciles on-hand stock against the movement ledger. Use to catch drift early.
    [HttpGet("integrity")]
    public IActionResult Integrity()
    {
        var validPartNos = _context.Parts.Select(p => p.PartNo).ToHashSet();
        var validPartIds = _context.Parts.Select(p => p.Id).ToHashSet();

        // ---- 1) Orphan scan: rows pointing at a Part that doesn't exist ----
        int OrphanByPartNo(IEnumerable<string?> vals) => vals.Count(v => v != null && v != "" && !validPartNos.Contains(v));

        var orphans = new List<object>();
        int totalOrphans = 0;
        void Add(string table, string col, int count)
        { if (count > 0) { orphans.Add(new { table, column = col, orphanCount = count }); totalOrphans += count; } }

        Add("StockMovements", "PartId", _context.StockMovements.Count(m => !validPartIds.Contains(m.PartId)));
        Add("GoodsReceiptLines", "PartNo", OrphanByPartNo(_context.GoodsReceiptLines.Select(x => x.PartNo)));
        Add("ReturnRequests", "PartNo", OrphanByPartNo(_context.ReturnRequests.Select(x => x.PartNo)));
        Add("StockTransfers", "PartNo", OrphanByPartNo(_context.StockTransfers.Select(x => x.PartNo)));
        Add("StockCountLines", "PartNo", OrphanByPartNo(_context.StockCountLines.Select(x => x.PartNo)));
        Add("DisposalRequests", "PartNo", OrphanByPartNo(_context.DisposalRequests.Select(x => x.PartNo)));
        Add("AtmModelParts", "PartNo", OrphanByPartNo(_context.AtmModelParts.Select(x => x.PartNo)));
        Add("EquivalentGroupMembers", "PartNo", OrphanByPartNo(_context.EquivalentGroupMembers.Select(x => x.PartNo)));
        Add("EquivalentParts", "OriginalPartNo", OrphanByPartNo(_context.EquivalentParts.Select(x => x.OriginalPartNo)));
        Add("EquivalentParts", "EquivalentPartNo", OrphanByPartNo(_context.EquivalentParts.Select(x => x.EquivalentPartNo)));
        Add("Tickets", "RequestedPartNo", OrphanByPartNo(_context.Tickets.Select(x => x.RequestedPartNo)));
        Add("Tickets", "ApprovedPartNo", OrphanByPartNo(_context.Tickets.Select(x => x.ApprovedPartNo)));

        // ---- 2) Reconciliation: on-hand stock vs net of the movement ledger ----
        // Every stock change writes a movement (To = in, From = out), so for each part
        // SUM(PartStock.Good+Def) should equal SUM(Qty into) - SUM(Qty out of) the system.
        var onHand = _context.PartStocks
            .GroupBy(s => s.PartId)
            .Select(g => new { PartId = g.Key, Qty = g.Sum(x => x.GoodQty + x.DefectiveQty) })
            .ToDictionary(x => x.PartId, x => x.Qty);

        var movedIn = _context.StockMovements.Where(m => m.ToLocationId != null)
            .GroupBy(m => m.PartId).Select(g => new { g.Key, Q = g.Sum(x => x.Qty) }).ToDictionary(x => x.Key, x => x.Q);
        var movedOut = _context.StockMovements.Where(m => m.FromLocationId != null)
            .GroupBy(m => m.PartId).Select(g => new { g.Key, Q = g.Sum(x => x.Qty) }).ToDictionary(x => x.Key, x => x.Q);

        var partNoById = _context.Parts.ToDictionary(p => p.Id, p => p.PartNo);
        var allPartIds = onHand.Keys.Union(movedIn.Keys).Union(movedOut.Keys);
        var mismatches = new List<object>();
        foreach (var pid in allPartIds)
        {
            var stock = onHand.GetValueOrDefault(pid, 0);
            var ledger = movedIn.GetValueOrDefault(pid, 0) - movedOut.GetValueOrDefault(pid, 0);
            if (stock != ledger)
                mismatches.Add(new {
                    partId = pid,
                    partNo = partNoById.GetValueOrDefault(pid, "(unknown)"),
                    onHandStock = stock, ledgerNet = ledger, diff = stock - ledger
                });
        }

        return Ok(new
        {
            healthy = totalOrphans == 0 && mismatches.Count == 0,
            checkedAt = DateTime.Now,
            orphans = new { totalRows = totalOrphans, byTable = orphans },
            reconciliation = new { mismatchedParts = mismatches.Count, details = mismatches }
        });
    }
}
