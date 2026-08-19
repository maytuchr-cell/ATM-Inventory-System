namespace Api.Models;

public class StockCountLine
{
    public int Id { get; set; }
    public int StockCountId { get; set; }
    public int PartId { get; set; }              // FK to Part
    public string PartNo { get; set; } = string.Empty; // snapshot
    public Part? Part { get; set; }
    public int LocationId { get; set; }
    public int SystemQty { get; set; }      // snapshot at count start
    public int? PhysicalQty { get; set; }   // entered by counter
    public int Variance => (PhysicalQty ?? SystemQty) - SystemQty;
    public bool AdjustApproved { get; set; }
    public string? Remarks { get; set; }

    public StockCount? StockCount { get; set; }
}
