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
    public int OutboundCount { get; set; }
    public int InboundRepairedCount { get; set; }

    public ICollection<DailyReportImportRow> Rows { get; set; } = new List<DailyReportImportRow>();
}

// One row from any imported sheet (Return inbound, Outbound Order, Inbound normal, etc.),
// with how it was matched.
public class DailyReportImportRow
{
    public int Id { get; set; }

    public int BatchId { get; set; }
    public DailyReportImportBatch? Batch { get; set; }

    public string SourceSheet { get; set; } = "Return inbound";
    public int RowIndex { get; set; } // position in the source sheet, for tracing back to the file
    public string PartNo { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string SerialNo { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string DhlStatus { get; set; } = string.Empty; // GOOD | BAD, as DHL reported it
    public string? Problem { get; set; } // DHL's free-text defect note, Bad rows only
    public string? FeName { get; set; } // technician name (e.g. from Outbound / Return sheets)

    // Aservice Case No. from the source row, when the export includes it (Mar 2026+ exports do;
    // older ones are null). Matches Ticket.ExternalTicketNo — see DailyReportController.Process.
    public string? CaseNo { get; set; }

    // ReturnConfirmed | RepairCompleted | StillInRepair | OutboundConfirmed | Unmatched
    public string MatchType { get; set; } = string.Empty;

    public int? TicketId { get; set; }        // set when MatchType = ReturnConfirmed or OutboundConfirmed
    public int? WithdrawBatchId { get; set; } // which ใบเบิก this row matched
    public int? PartUnitId { get; set; } // set when a PartUnit was created/updated for this row

    // Only meaningful when MatchType == Unmatched — true means the row still credited stock to
    // the central warehouse (Part existed in our system) even with no Ticket to tie it to; false
    // means nothing moved (Part No. from the file doesn't exist in our system at all). Drives
    // whether UndoRow can safely reverse an Unmatched row.
    public bool StockCredited { get; set; }

    public bool Undone { get; set; }
    public DateTime? UndoneAt { get; set; }
}
