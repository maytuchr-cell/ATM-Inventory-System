namespace Api.Models;

public class ReturnRequest
{
    public int Id { get; set; }
    public int TicketId { get; set; }            // required — every return must trace to a ticket
    public string PartNo { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good"; // Good | Defective
    public string SourceType { get; set; } = string.Empty; // Technician | GRG | LocalVendor
    public int LocationFromId { get; set; }       // where the part is being returned from (e.g. OL_TECHNICIAN)
    public int LocationToId { get; set; }          // where it lands (warehouse, or Scrap if Defective)
    public string ReturnedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Ticket? Ticket { get; set; }
}
