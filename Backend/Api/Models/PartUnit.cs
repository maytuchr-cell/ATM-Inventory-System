namespace Api.Models;

/// <summary>
/// An individual, serial-tracked physical unit of a Part.
/// Optional — only used for parts that must be tracked piece-by-piece
/// (e.g. boards with a serial number, items with an expiry date).
/// General consumable parts rely on PartStock quantities alone and have no PartUnit rows.
/// </summary>
public class PartUnit
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public int? LocationId { get; set; }            // where this specific unit currently sits
    public string SerialNo { get; set; } = string.Empty;   // unique per unit
    public string Condition { get; set; } = "Good"; // Good | Bad
    public DateTime? ExpiryDate { get; set; }       // per-lot expiry of this unit
    public bool IsUnrepairable { get; set; }        // flagged for disposal
    public DateTime ReceivedAt { get; set; } = DateTime.Now;  // used to compute aging
    public string Status { get; set; } = "InStock"; // InStock | Issued | Disposed

    public Part? Part { get; set; }
    public Location? Location { get; set; }
}
