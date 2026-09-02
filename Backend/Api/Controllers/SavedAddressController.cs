using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api.Controllers;

// A tech's own saved address book — CRUD scoped by TechEmail, used as a picker on both the
// withdraw and return forms so a tech doesn't have to retype the same address every time.
[ApiController]
[Route("[controller]")]
public class SavedAddressController : ControllerBase
{
    private readonly AppDbContext _context;

    public SavedAddressController(AppDbContext context)
    {
        _context = context;
    }

    // GET /SavedAddress?techEmail=...
    [HttpGet]
    public IActionResult GetAll([FromQuery] string techEmail)
    {
        if (string.IsNullOrWhiteSpace(techEmail)) return BadRequest(new { message = "techEmail is required." });

        var addresses = _context.SavedAddresses
            .Where(a => a.TechEmail == techEmail)
            .OrderByDescending(a => a.CreatedAt)
            .ToList();
        return Ok(addresses);
    }

    // POST /SavedAddress
    [HttpPost]
    public IActionResult Create([FromBody] SavedAddressDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TechEmail)) return BadRequest(new { message = "techEmail is required." });
        if (string.IsNullOrWhiteSpace(dto.Label)) return BadRequest(new { message = "Label is required." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        var address = new SavedAddress
        {
            TechEmail = dto.TechEmail,
            Label = dto.Label,
            Address = dto.Address
        };
        _context.SavedAddresses.Add(address);
        _context.SaveChanges();
        return Ok(address);
    }

    // PUT /SavedAddress/{id}
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] SavedAddressDto dto)
    {
        var address = _context.SavedAddresses.FirstOrDefault(a => a.SavedAddressId == id);
        if (address == null) return NotFound(new { message = "Address not found." });
        if (!string.IsNullOrWhiteSpace(dto.TechEmail) && dto.TechEmail != address.TechEmail)
            return BadRequest(new { message = "Cannot reassign an address to a different tech." });
        if (string.IsNullOrWhiteSpace(dto.Label)) return BadRequest(new { message = "Label is required." });
        if (string.IsNullOrWhiteSpace(dto.Address)) return BadRequest(new { message = "Address is required." });

        address.Label = dto.Label;
        address.Address = dto.Address;
        _context.SaveChanges();
        return Ok(address);
    }

    // DELETE /SavedAddress/{id}?techEmail=...
    [HttpDelete("{id}")]
    public IActionResult Delete(int id, [FromQuery] string techEmail)
    {
        var address = _context.SavedAddresses.FirstOrDefault(a => a.SavedAddressId == id);
        if (address == null) return NotFound(new { message = "Address not found." });
        if (!string.IsNullOrWhiteSpace(techEmail) && address.TechEmail != techEmail)
            return BadRequest(new { message = "Address belongs to a different tech." });

        _context.SavedAddresses.Remove(address);
        _context.SaveChanges();
        return Ok(new { message = "Deleted." });
    }
}

public class SavedAddressDto
{
    public string? TechEmail { get; set; }
    public string? Label { get; set; }
    public string? Address { get; set; }
}
