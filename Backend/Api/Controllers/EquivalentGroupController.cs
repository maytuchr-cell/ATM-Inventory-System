using Microsoft.AspNetCore.Mvc;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class EquivalentGroupController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CatalogImportService _catalogImport;
    public EquivalentGroupController(AppDbContext context, CatalogImportService catalogImport)
    {
        _context = context;
        _catalogImport = catalogImport;
    }

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

    // GET /api/EquivalentGroup/for-part/{partNo} — other parts in the same equivalent group(s),
    // for Admin to substitute in when the requested part is out of stock at Ticket-approval time.
    [HttpGet("for-part/{partNo}")]
    public IActionResult GetEquivalentsForPart(string partNo)
    {
        var groupIds = _context.EquivalentGroupMembers.Where(m => m.PartNo == partNo).Select(m => m.GroupId).ToHashSet();
        if (!groupIds.Any()) return Ok(new List<object>());

        var equivalentPartNos = _context.EquivalentGroupMembers
            .Where(m => groupIds.Contains(m.GroupId) && m.PartNo != partNo)
            .Select(m => m.PartNo)
            .Distinct()
            .ToList();

        var stockByPartId = _context.PartStocks
            .GroupBy(s => s.PartId)
            .Select(g => new { PartId = g.Key, Qty = g.Sum(x => x.GoodQty) })
            .ToDictionary(x => x.PartId, x => x.Qty);

        var result = _context.Parts
            .Where(p => equivalentPartNos.Contains(p.PartNo) && p.IsActive)
            .ToList()
            .Select(p => new { p.PartNo, p.PartName, stockQuantity = stockByPartId.GetValueOrDefault(p.Id, 0) });

        return Ok(result);
    }

    // POST /api/EquivalentGroup/import — bulk-create/merge equivalent groups from a GRG-style
    // catalog Excel file. Looks for a "Part Number" column and a "Same part no." column,
    // searched across the first 10 rows of EVERY sheet (not just the first) — an admin adding
    // an instructions/cover sheet ahead of the data is a reasonable thing to do, so the first
    // sheet that actually has both headers wins, not necessarily sheet 1.
    // "Same part no." may list several PartNos, one per line within the cell. Cells that
    // Excel stored as numbers (leading-zero-free) are converted to text before matching.
    // Real GRG catalog files carry embedded part photos and can run 30-50MB, past Kestrel's
    // 30MB default request-body cap — raise it for this one upload endpoint.
    [HttpPost("import")]
    [RequestSizeLimit(200_000_000)]
    public IActionResult ImportFromExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported." });

        try
        {
            using var ms = new MemoryStream();
            file.OpenReadStream().CopyTo(ms);
            var result = _catalogImport.LinkEquivalents(ms.ToArray(), file.FileName);

            return Ok(new
            {
                message = "Import complete.",
                rowsProcessed = result.RowsProcessed,
                groupsCreated = result.GroupsCreated,
                groupsMerged = result.GroupsMerged,
                membersAdded = result.MembersAdded,
                notFoundCount = result.NotFoundCount,
                notFoundSample = result.NotFoundSample
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Could not read the file: {ex.Message}" });
        }
    }

    // POST /api/EquivalentGroup/{id}/members
    [HttpPost("{id}/members")]
    public IActionResult AddMember(int id, [FromBody] MemberDto dto)
    {
        if (!_context.EquivalentGroups.Any(g => g.Id == id))
            return NotFound(new { message = "Group not found." });

        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.PartNo);
        if (part == null)
            return BadRequest(new { message = $"Part {dto.PartNo} not found." });

        if (_context.EquivalentGroupMembers.Any(m => m.GroupId == id && m.PartNo == dto.PartNo))
            return BadRequest(new { message = $"{dto.PartNo} is already in this group." });

        var member = new EquivalentGroupMember { GroupId = id, PartId = part.Id, PartNo = dto.PartNo };
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
