using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PartsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;

    public PartsController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // On-hand total for each part = SUM(PartStock.GoodQty). Computed from PartStock,
    // never stored on Part. Pass the part ids you need totals for.
    private Dictionary<int, int> StockTotals(IEnumerable<int> partIds)
    {
        var ids = partIds.ToList();
        return _context.PartStocks
            .Where(s => ids.Contains(s.PartId))
            .GroupBy(s => s.PartId)
            .Select(g => new { PartId = g.Key, Good = g.Sum(x => x.GoodQty) })
            .ToDictionary(x => x.PartId, x => x.Good);
    }

    // GET /api/Parts?categoryId=&isActive=&search=
    [HttpGet]
    public IActionResult GetAll([FromQuery] int? categoryId, [FromQuery] bool? isActive, [FromQuery] string? search)
    {
        var q = _context.Parts.Include(p => p.Category).AsQueryable();
        if (categoryId.HasValue) q = q.Where(p => p.CategoryId == categoryId);
        if (isActive.HasValue)   q = q.Where(p => p.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.PartName.Contains(search) || p.PartNo.Contains(search));

        var parts = q.OrderBy(p => p.PartNo).ToList();

        // on-hand totals from PartStock (source of truth)
        var stockTotals = StockTotals(parts.Select(p => p.Id));

        // attach known serial numbers from StockMovements
        var serialMap = _context.StockMovements
            .Where(m => m.SerialNo != null && m.SerialNo != "")
            .GroupBy(m => m.PartNo)
            .ToDictionary(g => g.Key, g => g.Select(m => m.SerialNo!).Distinct().ToList());

        var result = parts.Select(p => new {
            p.Id, p.PartNo, p.PartName, p.OrderNumber, p.Unit,
            StockQuantity = stockTotals.GetValueOrDefault(p.Id, 0),
            p.CategoryId, p.MinStock, p.MaxStock,
            p.ReorderPoint, p.CostPerUnit, p.CatalogueRef, p.SerialNo,
            p.MainUnit, p.Remark, p.ImagePath, p.Zone, p.DeviceType, p.AddedBy, p.Lot, p.Project, p.AddedDate,
            p.IsActive, p.CreatedAt, p.UpdatedAt,
            category = p.Category == null ? null : new { p.Category.Id, p.Category.Name },
            serialNos = serialMap.ContainsKey(p.PartNo) ? serialMap[p.PartNo] : new List<string>()
        });

        return Ok(result);
    }

    // GET /api/Parts/{id}
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var part = _context.Parts.Include(p => p.Category).FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        // on-hand total + per-location breakdown, computed from PartStock
        var byLocation = _context.PartStocks
            .Where(s => s.PartId == id)
            .Include(s => s.Location)
            .Select(s => new {
                locationId = s.LocationId,
                location = s.Location == null ? null : s.Location.Name,
                s.GoodQty, s.DefectiveQty
            })
            .ToList();

        return Ok(new {
            part.Id, part.PartNo, part.PartName, part.OrderNumber, part.Unit, part.SerialNo,
            StockQuantity = byLocation.Sum(s => s.GoodQty),
            part.CategoryId, part.MinStock, part.MaxStock, part.ReorderPoint,
            part.CostPerUnit, part.CatalogueRef, part.MainUnit, part.Remark, part.ImagePath, part.Zone, part.DeviceType, part.AddedBy, part.Lot, part.Project, part.AddedDate,
            part.IsActive, part.CreatedAt, part.UpdatedAt,
            category = part.Category == null ? null : new { part.Category.Id, part.Category.Name },
            stockByLocation = byLocation
        });
    }

    // POST /api/Parts
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost]
    public IActionResult Create([FromBody] PartWriteDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });

        if (_context.Parts.Any(p => p.PartNo == dto.PartNo))
            return BadRequest(new { message = $"PartNo '{dto.PartNo}' already exists." });

        var part = MapFromDto(new Part(), dto);
        part.CreatedAt = DateTime.UtcNow;
        part.UpdatedAt = DateTime.UtcNow;
        _context.Parts.Add(part);
        _context.SaveChanges();

        // Initial stock (if any) becomes a PartStock row at the main warehouse + an opening
        // StockMovement — the same path every other stock change takes. No stored total.
        if (dto.StockQuantity > 0)
        {
            var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT")
                         ?? _context.Locations.FirstOrDefault();
            if (mainWh != null)
            {
                _stock.AdjustStock(part.PartNo, mainWh.Id, dto.StockQuantity, "Good",
                    "GR", "OpeningBalance", null, CurrentUser(), "Initial stock on part creation");
                _context.SaveChanges();
            }
        }

        WriteAudit("Part", part.Id.ToString(), "CREATE", null, part);
        return Ok(PartSummary(part));
    }

    // PUT /api/Parts/{id}
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] PartWriteDto dto)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });

        if (_context.Parts.Any(p => p.PartNo == dto.PartNo && p.Id != id))
            return BadRequest(new { message = $"PartNo '{dto.PartNo}' already used by another part." });

        var old = JsonSerializer.Serialize(part);
        MapFromDto(part, dto);
        part.UpdatedAt = DateTime.UtcNow;
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "UPDATE", old, part);
        return Ok(PartSummary(part));
    }

    // DELETE /api/Parts/{id}  — soft delete
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        var old = JsonSerializer.Serialize(part);
        part.IsActive = false;
        part.UpdatedAt = DateTime.UtcNow;
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "DELETE", old, part);
        return Ok(new { message = "Part deactivated." });
    }

    // PATCH /api/Parts/{id}/restore
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPatch("{id}/restore")]
    public IActionResult Restore(int id)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        part.IsActive = true;
        part.UpdatedAt = DateTime.UtcNow;
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "UPDATE", null, part);
        return Ok(new { message = "Part restored." });
    }

    // Returns an error message if the DTO is invalid, otherwise null.
    private static string? Validate(PartWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PartNo))
            return "Part No. is required.";
        if (string.IsNullOrWhiteSpace(dto.PartName))
            return "Part Name is required.";
        if (dto.MinStock < 0 || dto.MaxStock < 0 || dto.ReorderPoint < 0)
            return "Min / Max / Reorder values cannot be negative.";
        if (dto.MinStock > dto.MaxStock)
            return $"Min Stock ({dto.MinStock}) cannot be greater than Max Stock ({dto.MaxStock}).";
        if (dto.ReorderPoint > dto.MaxStock)
            return $"Reorder Point ({dto.ReorderPoint}) cannot be greater than Max Stock ({dto.MaxStock}).";
        if (dto.StockQuantity < 0)
            return "Stock Quantity cannot be negative.";
        if (dto.CostPerUnit.HasValue && dto.CostPerUnit < 0)
            return "Cost per unit cannot be negative.";
        return null;
    }

    private static Part MapFromDto(Part part, PartWriteDto dto)
    {
        part.PartNo       = dto.PartNo;
        part.PartName     = dto.PartName;
        part.OrderNumber  = dto.OrderNumber ?? string.Empty;
        part.Unit         = dto.Unit ?? "pcs";
        part.SerialNo     = dto.SerialNo;
        // On-hand stock lives in PartStock, not on Part — initial stock is handled in Create()
        // via StockService, and all later changes go through GR/Issue/Return/Transfer/Disposal.
        part.CategoryId   = dto.CategoryId;
        part.CatalogueRef = dto.CatalogueRef;
        part.MinStock     = dto.MinStock;
        part.MaxStock     = dto.MaxStock;
        part.ReorderPoint = dto.ReorderPoint;
        part.TrackingNumber = dto.TrackingNumber;
        part.Aging        = dto.Aging;
        part.CostPerUnit  = dto.CostPerUnit;
        part.ExpiryDate     = dto.ExpiryDate;
        part.IsUnrepairable = dto.IsUnrepairable;
        part.MainUnit       = dto.MainUnit;
        part.Remark         = dto.Remark;
        if (dto.ImagePath != null) part.ImagePath = dto.ImagePath;
        part.Zone           = dto.Zone;
        part.DeviceType     = dto.DeviceType;
        part.AddedBy        = dto.AddedBy;
        part.Lot            = dto.Lot;
        part.Project        = dto.Project;
        part.AddedDate      = dto.AddedDate ?? DateTime.Now;
        return part;
    }

    // Clean response shape for a single part (avoids serializing the Stocks navigation cycle).
    private object PartSummary(Part p) => new
    {
        p.Id, p.PartNo, p.PartName, p.OrderNumber, p.Unit, p.SerialNo,
        StockQuantity = _context.PartStocks.Where(s => s.PartId == p.Id).Sum(s => s.GoodQty),
        p.CategoryId, p.MinStock, p.MaxStock, p.ReorderPoint,
        p.CostPerUnit, p.CatalogueRef, p.MainUnit, p.Remark, p.ImagePath, p.Zone, p.DeviceType, p.AddedBy, p.Lot, p.Project, p.AddedDate,
        p.IsActive, p.CreatedAt, p.UpdatedAt
    };

    private string CurrentUser() =>
        User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
        ?? User?.Identity?.Name
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? "system";

    private string CurrentUserId() =>
        User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? "system";

    private static readonly JsonSerializerOptions _auditJson = new()
    { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };

    private void WriteAudit(string entityType, string entityId, string action, string? oldValues, object? newValues)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId   = entityId,
            Action     = action,
            OldValues  = oldValues,
            NewValues  = newValues != null ? JsonSerializer.Serialize(newValues, _auditJson) : null,
            UserId     = CurrentUserId(),
            UserName   = CurrentUser(),
            Timestamp  = DateTime.UtcNow
        });
        _context.SaveChanges();
    }
}

public class PartWriteDto
{
    public string PartNo { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string? OrderNumber { get; set; }
    public string? Unit { get; set; }
    public string? SerialNo { get; set; }
    public int StockQuantity { get; set; }
    public int? CategoryId { get; set; }
    public string? CatalogueRef { get; set; }
    public int MinStock { get; set; } = 1;
    public int MaxStock { get; set; } = 100;
    public int ReorderPoint { get; set; } = 3;
    public string? TrackingNumber { get; set; }
    public int? Aging { get; set; }
    public decimal? CostPerUnit { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsUnrepairable { get; set; }
    public string? MainUnit { get; set; }
    public string? Remark { get; set; }
    public string? ImagePath { get; set; }
    public string? Zone { get; set; }
    public string? DeviceType { get; set; }
    public string? AddedBy { get; set; }
    public string? Lot { get; set; }
    public string? Project { get; set; }
    public DateTime? AddedDate { get; set; }
}
