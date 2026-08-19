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
        return Ok(q.OrderBy(l => l.Name).ToList());
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
