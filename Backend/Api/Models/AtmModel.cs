namespace Api.Models;

public class AtmModel
{
    public int Id { get; set; }
    public string ModelCode { get; set; } = string.Empty;   // e.g. "NCR-6622"
    public string ModelName { get; set; } = string.Empty;   // e.g. "NCR SelfServ 6622"
    public string? Manufacturer { get; set; }               // e.g. "NCR"
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<AtmModelPart> CompatibleParts { get; set; } = new List<AtmModelPart>();
}

public class AtmModelPart
{
    public int Id { get; set; }
    public int AtmModelId { get; set; }
    public int PartId { get; set; }              // FK to Part
    public string PartNo { get; set; } = string.Empty; // snapshot
    public Part? Part { get; set; }

    public AtmModel? AtmModel { get; set; }
}
