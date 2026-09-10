using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class LocationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly AuditService _audit;

    public LocationsController(AppDbContext context, AuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    [HttpGet]
    public IActionResult GetAll([FromQuery] string? locationType, [FromQuery] bool? isActive)
    {
        var q = _context.Locations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(locationType)) q = q.Where(l => l.LocationType == locationType);
        if (isActive.HasValue) q = q.Where(l => l.IsActive == isActive);
        var locations = q.OrderBy(l => l.Name).ToList();

        // Aggregate stock metrics per location
        var stocks = _context.PartStocks
            .GroupBy(s => s.LocationId)
            .Select(g => new
            {
                LocationId = g.Key,
                GoodQty = g.Sum(s => s.GoodQty),
                RepairQty = g.Sum(s => s.RepairQty + s.BadQty),
                PartTypesCount = g.Count(s => s.GoodQty > 0 || s.RepairQty > 0 || s.BadQty > 0)
            })
            .ToDictionary(x => x.LocationId);

        // Aggregate serial units count per location
        var units = _context.PartUnits
            .Where(u => u.Status != "Disposed" && u.LocationId.HasValue)
            .GroupBy(u => u.LocationId!.Value)
            .Select(g => new
            {
                LocationId = g.Key,
                SerialCount = g.Count(),
                InStockCount = g.Count(u => u.Status == "InStock"),
                IssuedCount = g.Count(u => u.Status == "Issued"),
                InRepairCount = g.Count(u => u.Status == "InRepair")
            })
            .ToDictionary(x => x.LocationId);

        var result = locations.Select(l =>
        {
            var st = stocks.GetValueOrDefault(l.Id);
            var un = units.GetValueOrDefault(l.Id);
            return new
            {
                l.Id,
                l.Name,
                l.Code,
                l.LocationType,
                l.IsActive,
                GoodQty = st?.GoodQty ?? 0,
                RepairQty = st?.RepairQty ?? 0,
                PartTypesCount = st?.PartTypesCount ?? 0,
                SerialUnitsCount = un?.SerialCount ?? 0,
                InStockUnitsCount = un?.InStockCount ?? 0,
                IssuedUnitsCount = un?.IssuedCount ?? 0,
                InRepairUnitsCount = un?.InRepairCount ?? 0
            };
        });

        return Ok(result);
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var loc = _context.Locations.FirstOrDefault(l => l.Id == id);
        if (loc == null) return NotFound();
        return Ok(loc);
    }

    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost]
    public IActionResult Create([FromBody] LocationWriteDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });
        if (_context.Locations.Any(l => l.Code == dto.Code))
            return BadRequest(new { message = $"Location code '{dto.Code}' already exists." });

        var loc = new Location { Name = dto.Name, Code = dto.Code, LocationType = dto.LocationType };
        _context.Locations.Add(loc);
        _context.SaveChanges();
        _audit.Log(User, "Location", loc.Id.ToString(), "CREATE", null, loc);
        return Ok(loc);
    }

    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] LocationWriteDto dto)
    {
        var loc = _context.Locations.FirstOrDefault(l => l.Id == id);
        if (loc == null) return NotFound();

        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });
        if (_context.Locations.Any(l => l.Code == dto.Code && l.Id != id))
            return BadRequest(new { message = $"Location code '{dto.Code}' already used by another location." });

        var old = JsonSerializer.Serialize(new { loc.Name, loc.Code, loc.LocationType });
        loc.Name = dto.Name;
        loc.Code = dto.Code;
        loc.LocationType = dto.LocationType;
        _context.SaveChanges();
        _audit.Log(User, "Location", id.ToString(), "UPDATE", old, loc);
        return Ok(loc);
    }

    private static string? Validate(LocationWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Location name is required.";
        if (string.IsNullOrWhiteSpace(dto.Code)) return "Location code is required.";
        if (string.IsNullOrWhiteSpace(dto.LocationType)) return "Location type is required.";
        return null;
    }

    [Authorize(Policy = "CanWriteMasterData")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var loc = _context.Locations.FirstOrDefault(l => l.Id == id);
        if (loc == null) return NotFound();
        loc.IsActive = false;
        _context.SaveChanges();
        _audit.Log(User, "Location", id.ToString(), "DELETE", null, loc);
        return Ok(new { message = "Location deactivated." });
    }
}

public class LocationWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
}
