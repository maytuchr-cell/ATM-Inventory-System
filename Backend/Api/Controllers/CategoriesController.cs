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
        return Ok(q.OrderBy(c => c.Name).ToList());
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var cat = _context.Categories.FirstOrDefault(c => c.Id == id);
        if (cat == null) return NotFound();
        return Ok(cat);
    }

    [HttpPost]
    public IActionResult Create([FromBody] CategoryWriteDto dto)
    {
        var cat = new Category { Name = dto.Name, Description = dto.Description };
        _context.Categories.Add(cat);
        _context.SaveChanges();
        return Ok(cat);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] CategoryWriteDto dto)
    {
        var cat = _context.Categories.FirstOrDefault(c => c.Id == id);
        if (cat == null) return NotFound();
        cat.Name = dto.Name;
        cat.Description = dto.Description;
        _context.SaveChanges();
        return Ok(cat);
    }

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
