namespace Api.Models;

// A tech's own saved address, reusable as a picker option on both the withdraw and return forms
// (WithdrawAddress / ReturnAddress) — scoped per-tech by TechEmail, never shared across techs.
public class SavedAddress
{
    public int SavedAddressId { get; set; }

    public string TechEmail { get; set; } = string.Empty;

    // Short name the tech picks it by, e.g. "บ้าน", "สาขาสีลม" — not unique, tech's own label.
    public string Label { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
