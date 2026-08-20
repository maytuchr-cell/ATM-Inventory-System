namespace Api.Models;

// เบิก/ยืม/คืน — Ticket synced from Aservice. One ticket carries both the withdraw leg and the
// return leg; TicketPartLine holds the actual part quantities for each leg (LineType).
public class Ticket
{
    public int TicketId { get; set; }

    public string ExternalTicketNo { get; set; } = string.Empty; // Aservice ticket no. — dedupe key for sync

    // Technician (no separate Technician table in this codebase — kept inline as on the old Ticket)
    public string TechEmail { get; set; } = string.Empty;
    public string TechName  { get; set; } = string.Empty;
    public string TechDept  { get; set; } = string.Empty;

    // null = synced from Aservice but the technician hasn't submitted a withdraw request yet.
    // รอ / เดินทาง / เบิก / คืน / Reject / Cancel
    public string? Status { get; set; }

    public string? RejectReason { get; set; }   // required only when Status = Reject
    public string? ApproverName { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public string? WithdrawAddress { get; set; }
    public string? ReturnAddress  { get; set; }

    // Free-text note from the tech on why they need these parts (e.g. "Card reader เสีย,
    // ปลั๊ก Sensor ขาด") — optional, shown to Admin alongside the withdraw request.
    public string? WithdrawDescription { get; set; }

    // Set once, at the moment a withdraw request is actually submitted (SubmitWithdraw) —
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

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<TicketPartLine> Lines { get; set; } = new List<TicketPartLine>();
}
