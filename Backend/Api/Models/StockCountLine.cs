namespace Api.Models;

public class StockCountLine
{
    public int Id { get; set; }
    public int StockCountId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public int SystemQty { get; set; }      // snapshot at count start
    public int? PhysicalQty { get; set; }   // entered by counter
    public int Variance => (PhysicalQty ?? SystemQty) - SystemQty;
    public bool AdjustApproved { get; set; }
    public string? Remarks { get; set; }

    public StockCount? StockCount { get; set; }
}
