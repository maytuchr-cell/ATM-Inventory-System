namespace Api.Models;

public class TicketAttachment
{
    public int TicketAttachmentId { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    // Set for Phase=="Withdraw" rows once the Level B rewrite lands — see TicketPartLine.WithdrawBatchId.
    public int? WithdrawBatchId { get; set; }
    public WithdrawBatch? WithdrawBatch { get; set; }

    public string Phase { get; set; } = "Withdraw"; // Withdraw | Return — which leg this photo was attached on

    public string FilePath { get; set; } = string.Empty; // served under /assets/tickets/...
    public string FileName { get; set; } = string.Empty; // original filename, for display

    public DateTime UploadedAt { get; set; } = DateTime.Now;
}
