namespace Api.Models;

// Single-row settings table. Row with Id=1 is the only one expected to exist.
public class SystemSettings
{
    public int Id { get; set; }
    public bool IsFrozen { get; set; }
    public int? ActiveStockCountId { get; set; }
}
