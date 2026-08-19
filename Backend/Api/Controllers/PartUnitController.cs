using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api.Controllers;

/// <summary>
/// Registry of individual, serial-tracked units for parts that need piece-level tracking
/// (serial numbers, per-lot expiry, repairability). Quantities still live in PartStock;
/// PartUnit records the identity and lifecycle of specific physical pieces.
/// </summary>
[ApiController]
[Route("[controller]")]
public class PartUnitController : ControllerBase
{
    private readonly AppDbContext _context;

    public PartUnitController(AppDbContext context) => _context = context;

    // GET /api/PartUnit?partNo=&partId=&status=
    [HttpGet]
    public IActionResult GetAll([FromQuery] string? partNo, [FromQuery] int? partId, [FromQuery] string? status)
    {
        var q = _context.PartUnits.Include(u => u.Part).Include(u => u.Location).AsQueryable();

        if (partId.HasValue) q = q.Where(u => u.PartId == partId);
        if (!string.IsNullOrWhiteSpace(partNo)) q = q.Where(u => u.Part != null && u.Part.PartNo == partNo);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(u => u.Status == status);

        var result = q.OrderBy(u => u.SerialNo).Select(u => new {
            u.Id, u.PartId, partNo = u.Part!.PartNo, partName = u.Part.PartName,
            u.LocationId, location = u.Location == null ? null : u.Location.Name,
            u.SerialNo, u.Condition, u.ExpiryDate, u.IsUnrepairable, u.ReceivedAt, u.Status
        }).ToList();

        return Ok(result);
    }

    // POST /api/PartUnit
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost]
    public IActionResult Create([FromBody] PartUnitWriteDto dto)
    {
        var error = Validate(dto, null);
        if (error != null) return BadRequest(new { message = error });

        var unit = new PartUnit
        {
            PartId         = dto.PartId,
            LocationId     = dto.LocationId,
            SerialNo       = dto.SerialNo.Trim(),
            Condition      = string.IsNullOrWhiteSpace(dto.Condition) ? "Good" : dto.Condition,
            ExpiryDate     = dto.ExpiryDate,
            IsUnrepairable = dto.IsUnrepairable,
            ReceivedAt     = dto.ReceivedAt ?? DateTime.Now,
            Status         = string.IsNullOrWhiteSpace(dto.Status) ? "InStock" : dto.Status
        };
        _context.PartUnits.Add(unit);
        _context.SaveChanges();
        return Ok(unit);
    }

    // PUT /api/PartUnit/{id}
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] PartUnitWriteDto dto)
    {
        var unit = _context.PartUnits.FirstOrDefault(u => u.Id == id);
        if (unit == null) return NotFound();

        var error = Validate(dto, id);
        if (error != null) return BadRequest(new { message = error });

        unit.PartId         = dto.PartId;
        unit.LocationId     = dto.LocationId;
        unit.SerialNo       = dto.SerialNo.Trim();
        unit.Condition      = string.IsNullOrWhiteSpace(dto.Condition) ? "Good" : dto.Condition;
        unit.ExpiryDate     = dto.ExpiryDate;
        unit.IsUnrepairable = dto.IsUnrepairable;
        if (dto.ReceivedAt.HasValue) unit.ReceivedAt = dto.ReceivedAt.Value;
        unit.Status         = string.IsNullOrWhiteSpace(dto.Status) ? unit.Status : dto.Status;
        _context.SaveChanges();
        return Ok(unit);
    }

    // DELETE /api/PartUnit/{id}
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var unit = _context.PartUnits.FirstOrDefault(u => u.Id == id);
        if (unit == null) return NotFound();
        _context.PartUnits.Remove(unit);
        _context.SaveChanges();
        return Ok(new { message = "Part unit deleted." });
    }

    private string? Validate(PartUnitWriteDto dto, int? excludeId)
    {
        if (dto.PartId <= 0 || !_context.Parts.Any(p => p.Id == dto.PartId))
            return "A valid Part is required.";
        if (string.IsNullOrWhiteSpace(dto.SerialNo))
            return "Serial No. is required.";
        var serial = dto.SerialNo.Trim();
        if (_context.PartUnits.Any(u => u.SerialNo == serial && (!excludeId.HasValue || u.Id != excludeId.Value)))
            return $"Serial No. '{serial}' already exists.";
        if (dto.LocationId.HasValue && !_context.Locations.Any(l => l.Id == dto.LocationId.Value))
            return "Location not found.";
        return null;
    }
}

public class PartUnitWriteDto
{
    public int PartId { get; set; }
    public int? LocationId { get; set; }
    public string SerialNo { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsUnrepairable { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? Status { get; set; }
}
