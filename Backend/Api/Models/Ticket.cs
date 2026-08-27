namespace Api.Models;

// เบิก/ยืม/คืน — Ticket synced from Aservice, one row per Case No. (ExternalTicketNo, unique).
// The withdraw leg is split out into WithdrawBatches — a Ticket can carry several independent
// ใบเบิก, each running its own รอ→รออะไหล่→รอส่งเมล DHL→เดินทาง→เบิก lifecycle. The fields below
// (Status/RejectReason/ApproverName/ApprovedAt/ReturnAddress/ReturnEmailSentAt) now describe ONLY
// the return leg — null until a return is submitted (SubmitReturn) against one of this Ticket's
// received (เบิก) batches. See WithdrawBatch.cs for the withdraw-leg fields.
public class Ticket
{
    public int TicketId { get; set; }

    public string ExternalTicketNo { get; set; } = string.Empty; // Aservice ticket no. — unique, dedupe key for sync

    // Technician (no separate Technician table in this codebase — kept inline as on the old Ticket)
    public string TechEmail { get; set; } = string.Empty;
    public string TechName  { get; set; } = string.Empty;
    public string TechDept  { get; set; } = string.Empty;

    // null = no return leg in flight (nothing submitted yet, or the last one fully closed).
    // รอ / อนุมัติคืน / กำลังเดินทางรับคืน / เดินทาง / คืน / Reject / Cancel
    public string? Status { get; set; }

    public string? RejectReason { get; set; }   // required only when Status = Reject
    public string? ApproverName { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public string? ReturnAddress  { get; set; }

    // Set when Admin confirms the DHL email asking them to come collect the return went out
    // (อนุมัติคืน → กำลังเดินทางรับคืน).
    public DateTime? ReturnEmailSentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // Return-leg part lines (LineType=="Return") only — withdraw lines live on each
    // WithdrawBatch's own Lines collection instead. Kept as a direct nav property (via
    // TicketPartLine.TicketId, which both Withdraw and Return lines still carry) mainly for
    // EF Core convenience; controllers filter by LineType where it matters.
    public ICollection<TicketPartLine> Lines { get; set; } = new List<TicketPartLine>();

    // "ใบเบิก" plural — see WithdrawBatch.cs.
    public ICollection<WithdrawBatch> WithdrawBatches { get; set; } = new List<WithdrawBatch>();
}
