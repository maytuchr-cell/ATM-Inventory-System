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

    [HttpPost]
    public IActionResult Create([FromBody] LocationWriteDto dto)
    {
        var loc = new Location { Name = dto.Name, Code = dto.Code, LocationType = dto.LocationType };
        _context.Locations.Add(loc);
        _context.SaveChanges();
        return Ok(loc);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] LocationWriteDto dto)
    {
        var loc = _context.Locations.FirstOrDefault(l => l.Id == id);
        if (loc == null) return NotFound();
        loc.Name = dto.Name;
        loc.Code = dto.Code;
        loc.LocationType = dto.LocationType;
        _context.SaveChanges();
        return Ok(loc);
    }

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
