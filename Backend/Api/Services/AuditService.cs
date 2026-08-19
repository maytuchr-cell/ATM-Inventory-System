using System.Security.Claims;
using System.Text.Json;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Central audit logger. Records who (from the JWT principal) did what, when —
/// shared by every controller so audit coverage and the user lookup stay consistent.
/// </summary>
public class AuditService
{
    private readonly AppDbContext _context;
    public AuditService(AppDbContext context) => _context = context;

    public static string UserName(ClaimsPrincipal? user) =>
        user?.FindFirst(ClaimTypes.Name)?.Value
        ?? user?.Identity?.Name
        ?? user?.FindFirst(ClaimTypes.Email)?.Value
        ?? "system";

    public static string UserId(ClaimsPrincipal? user) =>
        user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user?.FindFirst(ClaimTypes.Email)?.Value
        ?? "system";

    /// <summary>Writes an audit row and saves it.</summary>
    public void Log(ClaimsPrincipal? user, string entityType, string entityId, string action,
                    string? oldValues = null, object? newValues = null)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId   = entityId,
            Action     = action,
            OldValues  = oldValues,
            NewValues  = newValues != null ? JsonSerializer.Serialize(newValues,
                            new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles }) : null,
            UserId     = UserId(user),
            UserName   = UserName(user),
            Timestamp  = DateTime.UtcNow
        });
        _context.SaveChanges();
    }
}
