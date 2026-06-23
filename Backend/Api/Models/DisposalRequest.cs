namespace Api.Models;

public class DisposalRequest
{
    public int Id { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string? SerialNo { get; set; }
    public int LocationId { get; set; }     // where the part currently sits (normally Scrap by this point)
    public int Qty { get; set; } = 1;
    public string Status { get; set; } = "Pending"; // Pending|Approved|Disposed
    public string ReasonCode { get; set; } = string.Empty; // Expired|Unrepairable|Damaged|Other
    public string RequestedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? DisposedAt { get; set; }
}
