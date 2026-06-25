namespace Api.Models;

public class Part
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PartNo { get; set; } = string.Empty;     // Unique business key e.g. ATM-001
    public string PartName { get; set; } = string.Empty;
    public string Unit { get; set; } = "pcs";
    public string? SerialNo { get; set; }
    public int StockQuantity { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? CatalogueRef { get; set; }
    public int MinStock { get; set; } = 1;
    public int MaxStock { get; set; } = 100;
    public int ReorderPoint { get; set; } = 3;
    public string? TrackingNumber { get; set; }
    public int? Aging { get; set; }                        // days
    public decimal? CostPerUnit { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Disposal & Expiry (FR-DP-01)
    public DateTime? ExpiryDate { get; set; }
    public bool IsUnrepairable { get; set; }

    // Catalog fields (from GRG spare parts catalog)
    public string? MainUnit { get; set; }   // e.g. "Cabinet"
    public string? Remark { get; set; }      // free-text remark
    public string? ImagePath { get; set; }   // e.g. "/uploads/parts/208010040-H.jpg"
}
