namespace Api.Models;

public class TicketPartLine
{
    public int TicketPartLineId { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public int PartId { get; set; }
    public string PartNo { get; set; } = string.Empty; // snapshot, same pattern as ReturnRequest.PartNo
    public Part? Part { get; set; }

    public int Quantity { get; set; }

    public string LineType { get; set; } = "Withdraw"; // Withdraw | Return

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
