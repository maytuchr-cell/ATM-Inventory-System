namespace Api.Models;

// One "confirm" click on the Daily Report import page = one batch. Kept for history/audit and
// so a specific row can be undone later without guessing which import it came from.
public class DailyReportImportBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.Now;
    public string ImportedBy { get; set; } = string.Empty;

    public int TotalRows { get; set; }
    public int ReturnConfirmedCount { get; set; }
    public int RepairCompletedCount { get; set; }
    public int StillInRepairCount { get; set; }
    public int UnmatchedCount { get; set; }

    public ICollection<DailyReportImportRow> Rows { get; set; } = new List<DailyReportImportRow>();
}

// One row from the "Return inbound" sheet, with how it was matched. See
// DailyReportController.MatchRow for the matching rules this records the outcome of.
public class DailyReportImportRow
{
    public int Id { get; set; }

    public int BatchId { get; set; }
    public DailyReportImportBatch? Batch { get; set; }

    public int RowIndex { get; set; } // position in the source sheet, for tracing back to the file
    public string PartNo { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string SerialNo { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string DhlStatus { get; set; } = string.Empty; // GOOD | BAD, as DHL reported it
    public string? Problem { get; set; } // DHL's free-text defect note, Bad rows only

    // Aservice Case No. from the source row, when the export includes it (Mar 2026+ exports do;
    // older ones are null). Matches Ticket.ExternalTicketNo — see DailyReportController.Process.
    public string? CaseNo { get; set; }

    // ReturnConfirmed | RepairCompleted | StillInRepair | Unmatched — see DailyReportController.
    public string MatchType { get; set; } = string.Empty;

    public int? TicketId { get; set; }   // set when MatchType = ReturnConfirmed
    public int? PartUnitId { get; set; } // set when a PartUnit was created/updated for this row

    public bool Undone { get; set; }
    public DateTime? UndoneAt { get; set; }
}
