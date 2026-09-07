namespace Api.Models;

// One row of the "Contact list DataOne FE" reference sheet DHL/Ops maintains — DHL's own
// FE ID (site/route code, e.g. "Center 04") mapped to that site's default address and the
// person currently assigned there. Refreshed wholesale by re-importing the sheet (upsert by
// FeId), never hand-edited — see FeContactController.Import.
public class FeContact
{
    public int Id { get; set; }

    public string FeId { get; set; } = string.Empty;

    public string FeName { get; set; } = string.Empty;

    public string? Tel { get; set; }

    public string Address { get; set; } = string.Empty;

    public string? Postcode { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
