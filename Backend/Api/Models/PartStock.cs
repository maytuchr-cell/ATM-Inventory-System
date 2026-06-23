namespace Api.Models;

public class PartStock
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public int LocationId { get; set; }
    public int GoodQty { get; set; }
    public int DefectiveQty { get; set; }

    public Part? Part { get; set; }
    public Location? Location { get; set; }
}
