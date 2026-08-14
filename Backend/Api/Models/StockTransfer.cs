namespace Api.Models;

public class StockTransfer
{
    public int Id { get; set; }
    public int PartId { get; set; }              // FK to Part
    public string PartNo { get; set; } = string.Empty; // snapshot
    public Part? Part { get; set; }
    public int Qty { get; set; }
    public string Condition { get; set; } = "Good"; // Good | Bad
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending|Approved|InTransit|Received
    public string RequestedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
}
