using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class AuditLogController : ControllerBase
{
    private readonly AppDbContext _context;
    public AuditLogController(AppDbContext context) => _context = context;

    // GET /api/AuditLog — every AuditLog row, newest first. Small enough at this scale
    // (a few hundred rows) to filter/search client-side, same pattern as admin-history.html.
    [HttpGet]
    public IActionResult GetAll()
    {
        var logs = _context.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new
            {
                a.Id, a.EntityType, a.EntityId, a.Action,
                a.OldValues, a.NewValues,
                a.UserId, a.UserName, a.Timestamp
            })
            .ToList();
        return Ok(logs);
    }

    // GET /api/AuditLog/entity-types — distinct EntityType values seen so far, for the filter
    // dropdown (avoids hardcoding the list on the frontend as new entity types get logged).
    [HttpGet("entity-types")]
    public IActionResult GetEntityTypes()
    {
        var types = _context.AuditLogs.Select(a => a.EntityType).Distinct().OrderBy(t => t).ToList();
        return Ok(types);
    }
}
