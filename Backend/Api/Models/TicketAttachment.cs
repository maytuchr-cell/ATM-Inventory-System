namespace Api.Models;

public class TicketAttachment
{
    public int TicketAttachmentId { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    // Which ใบเบิก (WithdrawBatch) this photo belongs to — set for both Phase=="Withdraw" and
    // Phase=="Return" now that the return leg is batch-scoped, see TicketPartLine.WithdrawBatchId.
    public int? WithdrawBatchId { get; set; }
    public WithdrawBatch? WithdrawBatch { get; set; }

    public string Phase { get; set; } = "Withdraw"; // Withdraw | Return — which leg this photo was attached on

    public string FilePath { get; set; } = string.Empty; // served under /assets/tickets/...
    public string FileName { get; set; } = string.Empty; // original filename, for display

    public DateTime UploadedAt { get; set; } = DateTime.Now;
}
