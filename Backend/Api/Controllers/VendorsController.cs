using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VendorsController : ControllerBase
{
    private readonly AppDbContext _context;

    public VendorsController(AppDbContext context) => _context = context;

    [HttpGet]
    public IActionResult GetAll([FromQuery] string? vendorType, [FromQuery] bool? isActive)
    {
        var q = _context.Vendors.AsQueryable();
        if (!string.IsNullOrWhiteSpace(vendorType)) q = q.Where(v => v.VendorType == vendorType);
        if (isActive.HasValue) q = q.Where(v => v.IsActive == isActive);
        return Ok(q.OrderBy(v => v.Name).ToList());
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var vendor = _context.Vendors.FirstOrDefault(v => v.Id == id);
        if (vendor == null) return NotFound();
        return Ok(vendor);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult Create([FromBody] VendorWriteDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });
        if (_context.Vendors.Any(v => v.Code == dto.Code))
            return BadRequest(new { message = $"Vendor code '{dto.Code}' already exists." });

        var vendor = new Vendor { Name = dto.Name, Code = dto.Code, VendorType = dto.VendorType, ContactInfo = dto.ContactInfo };
        _context.Vendors.Add(vendor);
        _context.SaveChanges();
        return Ok(vendor);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] VendorWriteDto dto)
    {
        var vendor = _context.Vendors.FirstOrDefault(v => v.Id == id);
        if (vendor == null) return NotFound();

        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });
        if (_context.Vendors.Any(v => v.Code == dto.Code && v.Id != id))
            return BadRequest(new { message = $"Vendor code '{dto.Code}' already used by another vendor." });

        vendor.Name = dto.Name;
        vendor.Code = dto.Code;
        vendor.VendorType = dto.VendorType;
        vendor.ContactInfo = dto.ContactInfo;
        _context.SaveChanges();
        return Ok(vendor);
    }

    private static string? Validate(VendorWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Vendor name is required.";
        if (string.IsNullOrWhiteSpace(dto.Code)) return "Vendor code is required.";
        if (string.IsNullOrWhiteSpace(dto.VendorType)) return "Vendor type is required.";
        return null;
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var vendor = _context.Vendors.FirstOrDefault(v => v.Id == id);
        if (vendor == null) return NotFound();
        vendor.IsActive = false;
        _context.SaveChanges();
        return Ok(new { message = "Vendor deactivated." });
    }
}

public class VendorWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string VendorType { get; set; } = string.Empty;
    public string? ContactInfo { get; set; }
}
