namespace Api.Models;

public class StockCount
{
    public int Id { get; set; }
    public string CountType { get; set; } = "Cycle"; // Cycle | Annual
    public string Period { get; set; } = string.Empty; // e.g. "2026-Q1" or "2026-06"
    public string Status { get; set; } = "Draft"; // Draft|InProgress|Completed
    public bool IsSystemFrozen { get; set; }
    public string StartedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}
