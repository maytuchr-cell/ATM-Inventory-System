using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LocationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public LocationsController(AppDbContext context) => _context = context;

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

    [Authorize(Roles = "Admin")]
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
        return Ok(loc);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] LocationWriteDto dto)
    {
        var loc = _context.Locations.FirstOrDefault(l => l.Id == id);
        if (loc == null) return NotFound();

        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });
        if (_context.Locations.Any(l => l.Code == dto.Code && l.Id != id))
            return BadRequest(new { message = $"Location code '{dto.Code}' already used by another location." });

        loc.Name = dto.Name;
        loc.Code = dto.Code;
        loc.LocationType = dto.LocationType;
        _context.SaveChanges();
        return Ok(loc);
    }

    private static string? Validate(LocationWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Location name is required.";
        if (string.IsNullOrWhiteSpace(dto.Code)) return "Location code is required.";
        if (string.IsNullOrWhiteSpace(dto.LocationType)) return "Location type is required.";
        return null;
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var loc = _context.Locations.FirstOrDefault(l => l.Id == id);
        if (loc == null) return NotFound();
        loc.IsActive = false;
        _context.SaveChanges();
        return Ok(new { message = "Location deactivated." });
    }
}

public class LocationWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
}
