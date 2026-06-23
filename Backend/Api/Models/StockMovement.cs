namespace Api.Models;

public class StockMovement
{
    public int Id { get; set; }
    public string MovementType { get; set; } = string.Empty; // GR|Issue|Return|Transfer|Disposal|Adjustment
    public string PartNo { get; set; } = string.Empty;
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public int Qty { get; set; }
    public string Condition { get; set; } = "Good"; // Good|Defective
    public string? RefType { get; set; }   // Ticket|GoodsReceipt|Transfer|Disposal|StockCount
    public string? RefId { get; set; }
    public decimal? Cost { get; set; }
    public string? SerialNo { get; set; }
    public string? Remarks { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
