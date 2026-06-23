using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PartsController : ControllerBase
{
    private readonly AppDbContext _context;

    public PartsController(AppDbContext context) => _context = context;

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

        // attach known serial numbers from StockMovements
        var serialMap = _context.StockMovements
            .Where(m => m.SerialNo != null && m.SerialNo != "")
            .GroupBy(m => m.PartNo)
            .ToDictionary(g => g.Key, g => g.Select(m => m.SerialNo!).Distinct().ToList());

        var result = parts.Select(p => new {
            p.Id, p.PartNo, p.PartName, p.OrderNumber, p.Unit,
            p.StockQuantity, p.CategoryId, p.MinStock, p.MaxStock,
            p.ReorderPoint, p.CostPerUnit, p.CatalogueRef, p.SerialNo,
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
        return Ok(part);
    }

    // POST /api/Parts
    [HttpPost]
    public IActionResult Create([FromBody] PartWriteDto dto)
    {
        if (_context.Parts.Any(p => p.PartNo == dto.PartNo))
            return BadRequest(new { message = $"PartNo '{dto.PartNo}' already exists." });

        var part = MapFromDto(new Part(), dto);
        part.StockQuantity = dto.StockQuantity; // initial stock — no PartStock history exists yet for a brand-new part
        part.CreatedAt = DateTime.UtcNow;
        part.UpdatedAt = DateTime.UtcNow;
        _context.Parts.Add(part);
        _context.SaveChanges();

        WriteAudit("Part", part.Id.ToString(), "CREATE", null, part);
        return Ok(part);
    }

    // PUT /api/Parts/{id}
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] PartWriteDto dto)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        if (_context.Parts.Any(p => p.PartNo == dto.PartNo && p.Id != id))
            return BadRequest(new { message = $"PartNo '{dto.PartNo}' already used by another part." });

        var old = JsonSerializer.Serialize(part);
        MapFromDto(part, dto);
        part.UpdatedAt = DateTime.UtcNow;
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "UPDATE", old, part);
        return Ok(part);
    }

    // DELETE /api/Parts/{id}  — soft delete
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

    private static Part MapFromDto(Part part, PartWriteDto dto)
    {
        part.PartNo       = dto.PartNo;
        part.PartName     = dto.PartName;
        part.OrderNumber  = dto.OrderNumber ?? string.Empty;
        part.Unit         = dto.Unit ?? "pcs";
        part.SerialNo     = dto.SerialNo;
        // StockQuantity is intentionally NOT set here — it's a denormalized total owned by
        // StockService, kept in sync from PartStock via GR/Issue/Return/Transfer/Disposal/Adjustment.
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
        return part;
    }

    private void WriteAudit(string entityType, string entityId, string action, string? oldValues, object? newValues)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId   = entityId,
            Action     = action,
            OldValues  = oldValues,
            NewValues  = newValues != null ? JsonSerializer.Serialize(newValues) : null,
            UserId     = "system",
            UserName   = "system",
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
}
