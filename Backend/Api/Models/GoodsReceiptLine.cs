namespace Api.Models;

public class GoodsReceiptLine
{
    public int Id { get; set; }
    public int GoodsReceiptId { get; set; }
    public int PartId { get; set; }                  // FK to Part
    public string PartNo { get; set; } = string.Empty; // snapshot of the part number
    public Part? Part { get; set; }
    public int Qty { get; set; }
    public string Condition { get; set; } = "Good"; // Good | Bad
    public string? SerialNo { get; set; }
    public bool IsManualAdjust { get; set; }
    public string? Remarks { get; set; }

    public GoodsReceipt? GoodsReceipt { get; set; }
}
