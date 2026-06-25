using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoriesController(AppDbContext context) => _context = context;

    [HttpGet]
    public IActionResult GetAll([FromQuery] bool? isActive)
    {
        var q = _context.Categories.AsQueryable();
        if (isActive.HasValue) q = q.Where(c => c.IsActive == isActive);
        var cats = q.OrderBy(c => c.Name).ToList();

        var partCounts = _context.Parts
            .Where(p => p.CategoryId != null && p.IsActive)
            .GroupBy(p => p.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = cats.Select(c => new {
            c.Id, c.Name, c.Description, c.IsActive,
            partCount = partCounts.ContainsKey(c.Id) ? partCounts[c.Id] : 0
        });

        return Ok(result);
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var cat = _context.Categories.FirstOrDefault(c => c.Id == id);
        if (cat == null) return NotFound();
        return Ok(cat);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult Create([FromBody] CategoryWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Category name is required." });
        if (_context.Categories.Any(c => c.Name == dto.Name))
            return BadRequest(new { message = $"Category '{dto.Name}' already exists." });

        var cat = new Category { Name = dto.Name, Description = dto.Description };
        _context.Categories.Add(cat);
        _context.SaveChanges();
        return Ok(cat);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] CategoryWriteDto dto)
    {
        var cat = _context.Categories.FirstOrDefault(c => c.Id == id);
        if (cat == null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Category name is required." });
        if (_context.Categories.Any(c => c.Name == dto.Name && c.Id != id))
            return BadRequest(new { message = $"Category '{dto.Name}' already used by another category." });

        cat.Name = dto.Name;
        cat.Description = dto.Description;
        _context.SaveChanges();
        return Ok(cat);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var cat = _context.Categories.FirstOrDefault(c => c.Id == id);
        if (cat == null) return NotFound();

        if (_context.Parts.Any(p => p.CategoryId == id && p.IsActive))
            return BadRequest(new { message = "Cannot delete: active parts are assigned to this category." });

        cat.IsActive = false;
        _context.SaveChanges();
        return Ok(new { message = "Category deactivated." });
    }
}

public class CategoryWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
