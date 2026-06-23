using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public class StockService
{
    private readonly AppDbContext _context;

    public StockService(AppDbContext context) => _context = context;

    /// <summary>
    /// Adjusts stock for a part at a location by qtyDelta (positive=add, negative=remove),
    /// keeps PartStock, Part.StockQuantity (denormalized total) and StockMovement in sync.
    /// Does NOT call SaveChanges — caller controls the transaction boundary.
    /// </summary>
    public StockMovement AdjustStock(
        string partNo, int locationId, int qtyDelta, string condition,
        string movementType, string? refType, string? refId,
        string userName, string? remarks = null, decimal? cost = null, string? serialNo = null)
    {
        var part = _context.Parts.FirstOrDefault(p => p.PartNo == partNo)
            ?? throw new InvalidOperationException($"Part {partNo} not found.");

        var stock = _context.Set<PartStock>()
            .FirstOrDefault(s => s.PartId == part.Id && s.LocationId == locationId);

        if (stock == null)
        {
            stock = new PartStock { PartId = part.Id, LocationId = locationId, GoodQty = 0, DefectiveQty = 0 };
            _context.Set<PartStock>().Add(stock);
        }

        if (condition == "Defective")
            stock.DefectiveQty += qtyDelta;
        else
            stock.GoodQty += qtyDelta;

        if (stock.GoodQty < 0 || stock.DefectiveQty < 0)
            throw new InvalidOperationException($"Insufficient stock for {partNo} at location {locationId}.");

        // Persist the PartStock change first so the recalculation query below sees it.
        _context.SaveChanges();
        RecalculatePartTotal(part);

        var movement = new StockMovement
        {
            MovementType = movementType,
            PartNo = partNo,
            ToLocationId = qtyDelta >= 0 ? locationId : null,
            FromLocationId = qtyDelta < 0 ? locationId : null,
            Qty = Math.Abs(qtyDelta),
            Condition = condition,
            RefType = refType,
            RefId = refId,
            Cost = cost,
            SerialNo = serialNo,
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
        string movementType, string? refType, string? refId, string userName, string? remarks = null, string? serialNo = null)
    {
        var part = _context.Parts.FirstOrDefault(p => p.PartNo == partNo)
            ?? throw new InvalidOperationException($"Part {partNo} not found.");

        if (fromLocationId.HasValue)
        {
            var from = _context.Set<PartStock>()
                .FirstOrDefault(s => s.PartId == part.Id && s.LocationId == fromLocationId.Value)
                ?? throw new InvalidOperationException($"No stock for {partNo} at source location.");

            if (condition == "Defective") from.DefectiveQty -= qty; else from.GoodQty -= qty;
            if (from.GoodQty < 0 || from.DefectiveQty < 0)
                throw new InvalidOperationException($"Insufficient stock for {partNo} at source location.");
        }

        if (toLocationId.HasValue)
        {
            var to = _context.Set<PartStock>()
                .FirstOrDefault(s => s.PartId == part.Id && s.LocationId == toLocationId.Value);
            if (to == null)
            {
                to = new PartStock { PartId = part.Id, LocationId = toLocationId.Value, GoodQty = 0, DefectiveQty = 0 };
                _context.Set<PartStock>().Add(to);
            }
            if (condition == "Defective") to.DefectiveQty += qty; else to.GoodQty += qty;
        }

        // Persist the PartStock changes first so the recalculation query below sees them.
        _context.SaveChanges();
        RecalculatePartTotal(part);

        var movement = new StockMovement
        {
            MovementType = movementType,
            PartNo = partNo,
            FromLocationId = fromLocationId,
            ToLocationId = toLocationId,
            Qty = qty,
            Condition = condition,
            RefType = refType,
            RefId = refId,
            SerialNo = serialNo,
            Remarks = remarks,
            UserName = userName,
            Timestamp = DateTime.Now
        };
        _context.Set<StockMovement>().Add(movement);

        return movement;
    }

    private void RecalculatePartTotal(Part part)
    {
        part.StockQuantity = _context.Set<PartStock>()
            .Where(s => s.PartId == part.Id)
            .Sum(s => s.GoodQty);
        part.UpdatedAt = DateTime.Now;
    }
}
