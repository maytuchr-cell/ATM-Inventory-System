namespace Api.Models;

public class EquivalentPart
{
    public int Id { get; set; }
    public string OriginalPartNo { get; set; } = string.Empty;
    public string EquivalentPartNo { get; set; } = string.Empty;
}
