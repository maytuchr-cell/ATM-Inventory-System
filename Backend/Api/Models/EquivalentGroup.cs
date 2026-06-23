namespace Api.Models;

public class EquivalentGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<EquivalentGroupMember> Members { get; set; } = new List<EquivalentGroupMember>();
}

public class EquivalentGroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string PartNo { get; set; } = string.Empty;

    public EquivalentGroup? Group { get; set; }
}
