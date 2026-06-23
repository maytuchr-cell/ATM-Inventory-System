namespace Api.Models;

public class GoodsReceipt
{
    public int Id { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // GRG | LocalVendor
    public int? VendorId { get; set; }
    public string? RefDocument { get; set; } // Forecast/Lot ref or PO number
    public int LocationId { get; set; }       // target location received into
    public string ReceivedBy { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
    public decimal? HandlingCost { get; set; }

    public Vendor? Vendor { get; set; }
    public Location? Location { get; set; }
    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}
