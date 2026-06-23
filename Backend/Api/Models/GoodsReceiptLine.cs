namespace Api.Models;

public class GoodsReceiptLine
{
    public int Id { get; set; }
    public int GoodsReceiptId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string Condition { get; set; } = "Good"; // Good | Defective
    public string? SerialNo { get; set; }
    public bool IsManualAdjust { get; set; }
    public string? Remarks { get; set; }

    public GoodsReceipt? GoodsReceipt { get; set; }
}
