using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public class StockService
{
    private readonly AppDbContext _context;

    public StockService(AppDbContext context) => _context = context;

    // Finds the PartStock row for a part+location, checking the change tracker (Local) first
    // so repeated adjustments within the same request see rows added earlier but not yet saved
    // — this prevents creating duplicate (PartId, LocationId) rows that would break the unique index.
    private PartStock? FindStock(int partId, int locationId) =>
        _context.Set<PartStock>().Local.FirstOrDefault(s => s.PartId == partId && s.LocationId == locationId)
        ?? _context.Set<PartStock>().FirstOrDefault(s => s.PartId == partId && s.LocationId == locationId);

    /// <summary>
    /// Adjusts stock for a part at a location by qtyDelta (positive=add, negative=remove),
    /// updating PartStock (source of truth) and appending a StockMovement ledger row.
    /// Does NOT call SaveChanges — caller controls the transaction boundary (atomic with its own work).
    /// </summary>
    public StockMovement AdjustStock(
        string partNo, int locationId, int qtyDelta, string condition,
        string movementType, string? refType, string? refId,
        string userName, string? remarks = null, decimal? cost = null, string? serialNo = null, int? partUnitId = null)
    {
        var part = _context.Parts.FirstOrDefault(p => p.PartNo == partNo)
            ?? throw new InvalidOperationException($"Part {partNo} not found.");

        var stock = FindStock(part.Id, locationId);

        if (stock == null)
        {
            stock = new PartStock { PartId = part.Id, LocationId = locationId, GoodQty = 0, BadQty = 0 };
            _context.Set<PartStock>().Add(stock);
        }

        if (condition == "Bad")
            stock.BadQty += qtyDelta;
        else if (condition == "Repair")
            stock.RepairQty += qtyDelta; // DHL reported it Bad and took it for repair — see DailyReportController
        else
            stock.GoodQty += qtyDelta;

        if (stock.GoodQty < 0 || stock.BadQty < 0 || stock.RepairQty < 0)
            throw new InvalidOperationException($"Insufficient stock for {partNo} at location {locationId}.");

        stock.UpdatedAt = DateTime.Now;
        TouchPart(part);

        var movement = new StockMovement
        {
            MovementType = movementType,
            PartId = part.Id,
            PartNo = partNo,
            ToLocationId = qtyDelta >= 0 ? locationId : null,
            FromLocationId = qtyDelta < 0 ? locationId : null,
            Qty = Math.Abs(qtyDelta),
            Condition = condition,
            RefType = refType,
            RefId = refId,
            Cost = cost,
            SerialNo = serialNo,
            PartUnitId = partUnitId,
            Remarks = remarks,
            UserName = userName,
            Timestamp = DateTime.Now
        };
        _context.Set<StockMovement>().Add(movement);

        return movement;
    }

    /// <summary>
    /// Moves qty of a part between two locations (e.g. Transfer, Disposal-to-Scrap).
    /// Produces a single StockMovement row that records both From and To.
    /// </summary>
    public StockMovement MoveStock(
        string partNo, int? fromLocationId, int? toLocationId, int qty, string condition,
        string movementType, string? refType, string? refId, string userName, string? remarks = null, string? serialNo = null, int? partUnitId = null)
    {
        var part = _context.Parts.FirstOrDefault(p => p.PartNo == partNo)
            ?? throw new InvalidOperationException($"Part {partNo} not found.");

        if (fromLocationId.HasValue)
        {
            var from = FindStock(part.Id, fromLocationId.Value)
                ?? throw new InvalidOperationException($"No stock for {partNo} at source location.");

            if (condition == "Bad") from.BadQty -= qty; else from.GoodQty -= qty;
            if (from.GoodQty < 0 || from.BadQty < 0)
                throw new InvalidOperationException($"Insufficient stock for {partNo} at source location.");
            from.UpdatedAt = DateTime.Now;
        }

        if (toLocationId.HasValue)
        {
            var to = FindStock(part.Id, toLocationId.Value);
            if (to == null)
            {
                to = new PartStock { PartId = part.Id, LocationId = toLocationId.Value, GoodQty = 0, BadQty = 0 };
                _context.Set<PartStock>().Add(to);
            }
            if (condition == "Bad") to.BadQty += qty; else to.GoodQty += qty;
            to.UpdatedAt = DateTime.Now;
        }

        TouchPart(part);

        var movement = new StockMovement
        {
            MovementType = movementType,
            PartId = part.Id,
            PartNo = partNo,
            FromLocationId = fromLocationId,
            ToLocationId = toLocationId,
            Qty = qty,
            Condition = condition,
            RefType = refType,
            RefId = refId,
            SerialNo = serialNo,
            PartUnitId = partUnitId,
            Remarks = remarks,
            UserName = userName,
            Timestamp = DateTime.Now
        };
        _context.Set<StockMovement>().Add(movement);

        return movement;
    }

    // PartStock is the single source of truth for on-hand quantity — there is no
    // denormalized total to recompute. We only stamp the part as touched.
    private void TouchPart(Part part)
    {
        part.UpdatedAt = DateTime.Now;
    }
}
