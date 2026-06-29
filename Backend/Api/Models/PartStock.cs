namespace Api.Models;

public class PartStock
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public int LocationId { get; set; }
    public int GoodQty { get; set; }
    public int DefectiveQty { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;   // when this stock row last changed

    // Optimistic-concurrency token — bumped on every save (see AppDbContext.SaveChanges).
    // Prevents two simultaneous GR/Issue/adjust operations from silently overwriting each other.
    public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

    public Part? Part { get; set; }
    public Location? Location { get; set; }
}
