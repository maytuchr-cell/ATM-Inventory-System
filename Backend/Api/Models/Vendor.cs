namespace Api.Models;

public class Vendor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string VendorType { get; set; } = string.Empty; // "GRG" | "LOCAL"
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; } = true;
}
