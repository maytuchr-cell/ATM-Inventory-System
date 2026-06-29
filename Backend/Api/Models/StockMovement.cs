namespace Api.Models;

public class StockMovement
{
    public int Id { get; set; }
    public string MovementType { get; set; } = string.Empty; // GR|Issue|Return|Transfer|Disposal|Adjustment
    public int PartId { get; set; }                  // FK to Part — authoritative link (matches PartStock/PartUnit)
    public string PartNo { get; set; } = string.Empty; // snapshot of the part number at the time of the movement
    public Part? Part { get; set; }
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
