namespace Api.Models;

// One "ใบเบิก" — an independent withdraw request under a Ticket (Case No.). A Ticket can carry
// multiple WithdrawBatches (the tech can request more parts under the same Case No. while an
// earlier request is still open — "เบิกเพิ่ม"), each running its own รอ→รออะไหล่→รอส่งเมล DHL→
// เดินทาง→เบิก lifecycle independently. The return leg stays on Ticket itself (see Ticket.cs) —
// a return can pull parts from any of this Ticket's เบิก-status batches.
public class WithdrawBatch
{
    public int WithdrawBatchId { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    // รอ / รออะไหล่ / รอส่งเมล DHL / เดินทาง / เบิก / Reject / Cancel
    public string? Status { get; set; }

    public string? RejectReason { get; set; }   // required only when Status = Reject
    public string? ApproverName { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Set when Admin confirms the DHL email actually went out (รอส่งเมล DHL → เดินทาง) — the
    // tech's "expected delivery" estimate is +1 day from THIS, not ApprovedAt (auto-approve can
    // happen well before Admin gets around to emailing DHL).
    public DateTime? EmailSentAt { get; set; }

    public string? WithdrawAddress { get; set; }

    // Free-text note from the tech on why they need these parts (e.g. "Card reader เสีย,
    // ปลั๊ก Sensor ขาด") — optional, shown to Admin alongside the withdraw request.
    public string? WithdrawDescription { get; set; }

    // Set once, at the moment this batch is actually submitted (SubmitWithdraw) —
    // "WD-{year}-{5-digit running no.}", resets every calendar year. System-generated, the tech
    // never types it in.
    public string? WithdrawSlipNo { get; set; }

    // Tech-entered date/employee code on the withdraw form — informational, doesn't drive any
    // state transition (CreatedAt/UpdatedAt already track when the system recorded things).
    public DateTime? WithdrawDate { get; set; }
    public string? EmployeeCode { get; set; }

    // "Repair" (เบิกไปซ่อม — tied to a specific job/Case No., expected back after the job closes)
    // or "Keep" (เก็บ — the tech holds it as personal buffer stock, not tied to any job).
    public string? UsageStatus { get; set; }

    // Technical advisor the tech consulted before requesting this withdraw — null/omitted means
    // "ไม่มี" (didn't consult anyone).
    public string? TechSupportName { get; set; }

    // Logistics fields DHL's own Delivery Request Form needs (see ExportDhlExcel) — the tech
    // fills these in on the withdraw form since they're the one who knows the site/urgency.
    public DateTime? NeededByDate { get; set; }  // "วันที่ต้องการอะไหล่" — deadline the part must arrive by
    public string? FeId { get; set; }            // Field-engineer zone/route code, e.g. "Center 04"
    public string? Sla { get; set; }             // Delivery urgency tier, e.g. "Urgent 4 Hr (Cut-off 13:30)"
    public string? AtmCode { get; set; }         // Site/ATM machine code, e.g. "T091B030B950G262"

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<TicketPartLine> Lines { get; set; } = new List<TicketPartLine>();
    public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
}
