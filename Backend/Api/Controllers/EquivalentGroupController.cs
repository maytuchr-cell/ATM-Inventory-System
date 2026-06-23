using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EquivalentGroupController : ControllerBase
{
    private readonly AppDbContext _context;
    public EquivalentGroupController(AppDbContext context) => _context = context;

    // GET /api/EquivalentGroup
    [HttpGet]
    public IActionResult GetAll()
    {
        var groups = _context.EquivalentGroups.ToList();
        var members = _context.EquivalentGroupMembers.ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var result = groups.Select(g => new
        {
            g.Id, g.Name, g.Description, g.CreatedAt,
            members = members
                .Where(m => m.GroupId == g.Id)
                .Select(m => new { m.Id, m.PartNo, partName = partMap.GetValueOrDefault(m.PartNo, m.PartNo) })
                .ToList()
        });

        return Ok(result);
    }

    // POST /api/EquivalentGroup
    [HttpPost]
    public IActionResult Create([FromBody] GroupWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Group name is required." });

        var group = new EquivalentGroup { Name = dto.Name.Trim(), Description = dto.Description?.Trim() };
        _context.EquivalentGroups.Add(group);
        _context.SaveChanges();
        return Ok(new { message = "Group created.", group });
    }

    // PUT /api/EquivalentGroup/{id}
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] GroupWriteDto dto)
    {
        var group = _context.EquivalentGroups.FirstOrDefault(g => g.Id == id);
        if (group == null) return NotFound();

        group.Name = dto.Name?.Trim() ?? group.Name;
        group.Description = dto.Description?.Trim();
        _context.SaveChanges();
        return Ok(new { message = "Updated.", group });
    }

    // DELETE /api/EquivalentGroup/{id}
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var group = _context.EquivalentGroups.FirstOrDefault(g => g.Id == id);
        if (group == null) return NotFound();
        _context.EquivalentGroups.Remove(group);
        _context.SaveChanges();
        return Ok(new { message = "Deleted." });
    }

    // POST /api/EquivalentGroup/{id}/members
    [HttpPost("{id}/members")]
    public IActionResult AddMember(int id, [FromBody] MemberDto dto)
    {
        if (!_context.EquivalentGroups.Any(g => g.Id == id))
            return NotFound(new { message = "Group not found." });

        if (!_context.Parts.Any(p => p.PartNo == dto.PartNo))
            return BadRequest(new { message = $"Part {dto.PartNo} not found." });

        if (_context.EquivalentGroupMembers.Any(m => m.GroupId == id && m.PartNo == dto.PartNo))
            return BadRequest(new { message = $"{dto.PartNo} is already in this group." });

        var member = new EquivalentGroupMember { GroupId = id, PartNo = dto.PartNo };
        _context.EquivalentGroupMembers.Add(member);
        _context.SaveChanges();
        return Ok(new { message = "Member added.", member });
    }

    // DELETE /api/EquivalentGroup/{id}/members/{memberId}
    [HttpDelete("{id}/members/{memberId}")]
    public IActionResult RemoveMember(int id, int memberId)
    {
        var member = _context.EquivalentGroupMembers.FirstOrDefault(m => m.Id == memberId && m.GroupId == id);
        if (member == null) return NotFound();
        _context.EquivalentGroupMembers.Remove(member);
        _context.SaveChanges();
        return Ok(new { message = "Member removed." });
    }
}

public class GroupWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MemberDto
{
    public string PartNo { get; set; } = string.Empty;
}
