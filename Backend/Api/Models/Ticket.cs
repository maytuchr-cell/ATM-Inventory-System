namespace Api.Models;

public class Ticket
{
    public int TicketId { get; set; }

    // Technician info
    public string TechEmail { get; set; } = string.Empty;
    public string TechId { get; set; } = string.Empty;
    public string TechName { get; set; } = string.Empty;
    public string TechPhone { get; set; } = string.Empty;
    public string TechDept { get; set; } = string.Empty;

    // Part references (FK → Part.PartNo)
    public string? RequestedPartNo { get; set; }
    public string? ApprovedPartNo { get; set; }
    public Part? RequestedPartNav { get; set; }
    public Part? ApprovedPartNav { get; set; }

    // Defective part info (filled by tech)
    public string? FaultySerialNo  { get; set; }   // S/N of broken part
    public string? FaultyPartNo    { get; set; }   // Part number of broken part
    public string? MachineModel    { get; set; }   // ATM machine model
    public string? Description     { get; set; }   // Issue description
    public string? AttachmentPath  { get; set; }   // Uploaded image path

    // Issue & Withdrawal enhancement (FR-IW-01, FR-IW-05)
    public string? MainCause     { get; set; }   // Main fault cause referenced by the technician
    public decimal? LogisticsCost { get; set; }   // Actual cost incurred issuing this part

    public string Status { get; set; } = "Pending";

    // FR-MC-04: technician reports a part as Dead on Arrival right after receiving it
    public bool IsDOA { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? DueDate { get; set; }
}
