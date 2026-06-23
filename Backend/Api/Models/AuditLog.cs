namespace Api.Models;

public class AuditLog
{
    public int Id { get; set; }
    // Part | Category | Location | Vendor | Ticket
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    // CREATE | UPDATE | DELETE | APPROVE | REJECT
    public string Action { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
