namespace Api.Models;

public class TicketPartLine
{
    public int TicketPartLineId { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    // Set for LineType=="Withdraw" rows once the Level B rewrite lands — which ใบเบิก (WithdrawBatch)
    // this line belongs to. LineType=="Return" rows leave this null and are addressed by TicketId
    // directly (the return leg stays Ticket-scoped, not batch-scoped).
    public int? WithdrawBatchId { get; set; }
    public WithdrawBatch? WithdrawBatch { get; set; }

    public int PartId { get; set; }
    public string PartNo { get; set; } = string.Empty; // snapshot, same pattern as ReturnRequest.PartNo
    public Part? Part { get; set; }

    // Set once, the first time Admin substitutes this line for a registered equivalent
    // (see TicketController.SubstitutePart) — holds the part the tech originally requested,
    // so Admin/tech can still see "requested X, got Y" after PartNo is overwritten. Null
    // means the line was never substituted.
    public string? OriginalPartNo { get; set; }

    public int Quantity { get; set; }

    public string LineType { get; set; } = "Withdraw"; // Withdraw | Return

    // Return lines only — how much of Quantity has been confirmed physically received so far via
    // Daily Report imports (see DailyReportController). A DHL file may only cover part of what a
    // tech returned; the ticket only finalizes to คืน once every Return line's ConfirmedQty
    // reaches its Quantity. Withdraw lines never touch this.
    public int ConfirmedQty { get; set; }

    // Only meaningful on a Return line — the tech states what shape the part came back in.
    // Good/Bad land in the matching PartStock bucket; Lost means it never physically came
    // back (missing, or a non-circulating "baby part") so no stock is added at confirm-return.
    public string? Condition { get; set; } // Good | Bad | Lost

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
