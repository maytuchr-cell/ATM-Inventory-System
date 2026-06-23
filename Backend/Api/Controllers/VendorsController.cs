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

    [HttpPost]
    public IActionResult Create([FromBody] VendorWriteDto dto)
    {
        var vendor = new Vendor { Name = dto.Name, Code = dto.Code, VendorType = dto.VendorType, ContactInfo = dto.ContactInfo };
        _context.Vendors.Add(vendor);
        _context.SaveChanges();
        return Ok(vendor);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] VendorWriteDto dto)
    {
        var vendor = _context.Vendors.FirstOrDefault(v => v.Id == id);
        if (vendor == null) return NotFound();
        vendor.Name = dto.Name;
        vendor.Code = dto.Code;
        vendor.VendorType = dto.VendorType;
        vendor.ContactInfo = dto.ContactInfo;
        _context.SaveChanges();
        return Ok(vendor);
    }

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
