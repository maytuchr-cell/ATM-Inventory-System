using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AtmModelController : ControllerBase
{
    private readonly AppDbContext _context;
    public AtmModelController(AppDbContext context) => _context = context;

    // GET /api/AtmModel — list all active models with their compatible parts
    [HttpGet]
    public IActionResult GetAll([FromQuery] bool? isActive)
    {
        var query = _context.AtmModels.AsQueryable();
        if (isActive.HasValue) query = query.Where(m => m.IsActive == isActive.Value);

        var models = query.OrderBy(m => m.Manufacturer).ThenBy(m => m.ModelName).ToList();
        var parts  = _context.AtmModelParts.ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var result = models.Select(m => new
        {
            m.Id, m.ModelCode, m.ModelName, m.Manufacturer, m.Description, m.IsActive,
            compatibleParts = parts
                .Where(p => p.AtmModelId == m.Id)
                .Select(p => new
                {
                    p.Id,
                    p.PartNo,
                    partName = partMap.TryGetValue(p.PartNo, out var n) ? n : p.PartNo
                })
        });

        return Ok(result);
    }

    // GET /api/AtmModel/{id}
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var model = _context.AtmModels.FirstOrDefault(m => m.Id == id);
        if (model == null) return NotFound(new { message = "ATM model not found." });

        var parts   = _context.AtmModelParts.Where(p => p.AtmModelId == id).ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        return Ok(new
        {
            model.Id, model.ModelCode, model.ModelName, model.Manufacturer, model.Description, model.IsActive,
            compatibleParts = parts.Select(p => new
            {
                p.Id, p.PartNo,
                partName = partMap.TryGetValue(p.PartNo, out var n) ? n : p.PartNo
            })
        });
    }

    // GET /api/AtmModel/{id}/parts — return compatible part numbers for filtering dropdown
    [HttpGet("{id}/parts")]
    public IActionResult GetCompatibleParts(int id)
    {
        var model = _context.AtmModels.FirstOrDefault(m => m.Id == id && m.IsActive);
        if (model == null) return NotFound(new { message = "ATM model not found." });

        var partNos = _context.AtmModelParts
            .Where(p => p.AtmModelId == id)
            .Select(p => p.PartNo)
            .ToList();

        // If no parts assigned → no restriction (return all active parts)
        if (!partNos.Any())
        {
            var all = _context.Parts
                .Where(p => p.IsActive)
                .Select(p => new { p.PartNo, p.PartName, p.StockQuantity, p.Unit })
                .ToList();
            return Ok(new { restricted = false, parts = all });
        }

        var parts = _context.Parts
            .Where(p => p.IsActive && partNos.Contains(p.PartNo))
            .Select(p => new { p.PartNo, p.PartName, p.StockQuantity, p.Unit })
            .ToList();

        return Ok(new { restricted = true, parts });
    }

    // POST /api/AtmModel
    [HttpPost]
    public IActionResult Create([FromBody] AtmModelDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ModelCode) || string.IsNullOrWhiteSpace(dto.ModelName))
            return BadRequest(new { message = "ModelCode and ModelName are required." });

        if (_context.AtmModels.Any(m => m.ModelCode == dto.ModelCode))
            return BadRequest(new { message = $"Model code '{dto.ModelCode}' already exists." });

        var model = new AtmModel
        {
            ModelCode    = dto.ModelCode.Trim().ToUpper(),
            ModelName    = dto.ModelName.Trim(),
            Manufacturer = dto.Manufacturer?.Trim(),
            Description  = dto.Description?.Trim(),
            IsActive     = true
        };
        _context.AtmModels.Add(model);
        _context.SaveChanges();
        return Ok(new { message = "ATM model created.", model });
    }

    // PUT /api/AtmModel/{id}
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] AtmModelDto dto)
    {
        var model = _context.AtmModels.FirstOrDefault(m => m.Id == id);
        if (model == null) return NotFound(new { message = "ATM model not found." });

        if (_context.AtmModels.Any(m => m.ModelCode == dto.ModelCode && m.Id != id))
            return BadRequest(new { message = $"Model code '{dto.ModelCode}' is used by another model." });

        model.ModelCode    = dto.ModelCode.Trim().ToUpper();
        model.ModelName    = dto.ModelName.Trim();
        model.Manufacturer = dto.Manufacturer?.Trim();
        model.Description  = dto.Description?.Trim();
        if (dto.IsActive.HasValue) model.IsActive = dto.IsActive.Value;

        _context.SaveChanges();
        return Ok(new { message = "Updated.", model });
    }

    // POST /api/AtmModel/{id}/parts — add compatible part
    [HttpPost("{id}/parts")]
    public IActionResult AddPart(int id, [FromBody] AddPartDto dto)
    {
        var model = _context.AtmModels.FirstOrDefault(m => m.Id == id);
        if (model == null) return NotFound(new { message = "ATM model not found." });

        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.PartNo && p.IsActive);
        if (part == null) return BadRequest(new { message = $"Part '{dto.PartNo}' not found." });

        if (_context.AtmModelParts.Any(p => p.AtmModelId == id && p.PartNo == dto.PartNo))
            return BadRequest(new { message = "Part already in this model's list." });

        _context.AtmModelParts.Add(new AtmModelPart { AtmModelId = id, PartNo = dto.PartNo });
        _context.SaveChanges();
        return Ok(new { message = "Part added.", partNo = dto.PartNo, partName = part.PartName });
    }

    // DELETE /api/AtmModel/{id}/parts/{partId}
    [HttpDelete("{id}/parts/{partId}")]
    public IActionResult RemovePart(int id, int partId)
    {
        var entry = _context.AtmModelParts.FirstOrDefault(p => p.Id == partId && p.AtmModelId == id);
        if (entry == null) return NotFound(new { message = "Part entry not found." });

        _context.AtmModelParts.Remove(entry);
        _context.SaveChanges();
        return Ok(new { message = "Part removed." });
    }

    // DELETE /api/AtmModel/{id}
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var model = _context.AtmModels.FirstOrDefault(m => m.Id == id);
        if (model == null) return NotFound(new { message = "ATM model not found." });

        model.IsActive = false;
        _context.SaveChanges();
        return Ok(new { message = "ATM model deactivated." });
    }
}

public record AtmModelDto(string ModelCode, string ModelName, string? Manufacturer, string? Description, bool? IsActive);
public record AddPartDto(string PartNo);
