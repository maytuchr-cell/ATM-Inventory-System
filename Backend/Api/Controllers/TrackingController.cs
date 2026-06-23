using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackingController : ControllerBase
{
    private readonly AppDbContext _context;
    public TrackingController(AppDbContext context) => _context = context;

    // GET /api/Tracking/serial/{serialNo}
    // Returns a vertical timeline (oldest→newest) for a given Serial Number,
    // assembled from GoodsReceiptLines, StockMovements, and Tickets.
    [HttpGet("serial/{serialNo}")]
    public IActionResult GetBySerial(string serialNo)
    {
        if (string.IsNullOrWhiteSpace(serialNo))
            return BadRequest(new { message = "Serial No. is required." });

        var sn = serialNo.Trim();
        var locMap  = _context.Locations.ToDictionary(l => l.Id, l => l.Name);
        var events  = new List<TimelineEvent>();

        // ── GoodsReceiptLine — only for old GRs that pre-date SerialNo in StockMovement ──
        // New GRs populate SerialNo in StockMovement directly, so deduplicate by receiptId.
        var movementGrRefIds = _context.StockMovements
            .Where(m => m.SerialNo == sn && m.MovementType == "GR" && m.RefType == "GoodsReceipt")
            .Select(m => m.RefId)
            .ToHashSet();

        var grLines = _context.GoodsReceiptLines
            .Where(l => l.SerialNo == sn)
            .ToList();

        foreach (var line in grLines)
        {
            var gr = _context.GoodsReceipts.FirstOrDefault(g => g.Id == line.GoodsReceiptId);
            if (gr == null) continue;
            // Skip if this GR is already represented in StockMovements
            if (movementGrRefIds.Contains(gr.Id.ToString())) continue;

            var toLocName = locMap.GetValueOrDefault(gr.LocationId, "—");
            events.Add(new TimelineEvent
            {
                Timestamp   = gr.ReceivedAt,
                EventType   = "GR",
                Description = $"Received via Goods Receipt #{gr.ReceiptNo} ({gr.Source})",
                Location    = toLocName,
                Condition   = line.Condition,
                RefType     = "GoodsReceipt",
                RefId       = gr.ReceiptNo,
                UserName    = gr.ReceivedBy
            });
        }

        // ── StockMovements tagged with this S/N ───────────────────────────────
        var movements = _context.StockMovements
            .Where(m => m.SerialNo == sn)
            .OrderBy(m => m.Timestamp)
            .ToList();

        foreach (var m in movements)
        {
            var loc = m.ToLocationId.HasValue ? locMap.GetValueOrDefault(m.ToLocationId.Value, "—")
                    : m.FromLocationId.HasValue ? locMap.GetValueOrDefault(m.FromLocationId.Value, "—")
                    : "—";
            events.Add(new TimelineEvent
            {
                Timestamp   = m.Timestamp,
                EventType   = m.MovementType,
                Description = BuildMovementDescription(m, locMap),
                Location    = loc,
                Condition   = m.Condition,
                RefType     = m.RefType,
                RefId       = m.RefId,
                UserName    = m.UserName
            });
        }

        // ── Tickets with FaultySerialNo matching (broken-part submissions) ────
        var tickets = _context.Tickets
            .Where(t => t.FaultySerialNo == sn)
            .OrderBy(t => t.CreatedAt)
            .ToList();

        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        foreach (var t in tickets)
        {
            events.Add(new TimelineEvent
            {
                Timestamp   = t.CreatedAt,
                EventType   = "TicketSubmit",
                Description = $"Reported as faulty on Ticket TK-{t.TicketId:0000} by {t.TechName} ({t.TechDept})",
                Location    = "OL Technician",
                Condition   = "Defective",
                RefType     = "Ticket",
                RefId       = $"TK-{t.TicketId:0000}",
                UserName    = t.TechName
            });

            if (t.Status == "Received" || t.Status == "Approved")
            {
                events.Add(new TimelineEvent
                {
                    Timestamp   = t.ReceivedAt ?? t.CreatedAt,
                    EventType   = "Issue",
                    Description = $"New part issued: {t.ApprovedPartNo ?? "—"} for ticket TK-{t.TicketId:0000}",
                    Location    = "OL Technician",
                    Condition   = "Good",
                    RefType     = "Ticket",
                    RefId       = $"TK-{t.TicketId:0000}",
                    UserName    = t.TechName
                });
            }
        }

        if (!events.Any())
            return NotFound(new { message = $"No records found for Serial No. '{sn}'." });

        events = events.OrderBy(e => e.Timestamp).ToList();

        // Current status: last known location & condition
        var lastEvent    = events.Last();
        var currentStatus = new
        {
            location  = lastEvent.Location,
            condition = lastEvent.Condition,
            eventType = lastEvent.EventType,
            asOf      = lastEvent.Timestamp
        };

        return Ok(new { serialNo = sn, currentStatus, timeline = events });
    }

    private static string BuildMovementDescription(StockMovement m, Dictionary<int, string> locMap)
    {
        var from = m.FromLocationId.HasValue ? locMap.GetValueOrDefault(m.FromLocationId.Value, "?") : null;
        var to   = m.ToLocationId.HasValue   ? locMap.GetValueOrDefault(m.ToLocationId.Value,   "?") : null;

        return m.MovementType switch
        {
            "GR"         => $"Received into {to ?? "warehouse"} via Goods Receipt",
            "Issue"      => $"Issued from {from ?? "warehouse"} to technician{(m.RefId != null ? $" (Ticket #{m.RefId})" : "")}",
            "Return"     => $"Returned to {to ?? "warehouse"} in {m.Condition} condition",
            "Transfer"   => $"Transferred from {from ?? "?"} → {to ?? "?"}",
            "Disposal"   => $"Disposed at {from ?? "scrap yard"}{(m.Remarks != null ? $": {m.Remarks}" : "")}",
            "Adjustment" => $"Stock adjustment at {to ?? from ?? "?"} ({(m.Qty >= 0 ? "+" : "")}{m.Qty}){(m.Remarks != null ? $": {m.Remarks}" : "")}",
            _            => m.MovementType
        };
    }
}

public class TimelineEvent
{
    public DateTime Timestamp   { get; set; }
    public string EventType     { get; set; } = string.Empty;
    public string Description   { get; set; } = string.Empty;
    public string Location      { get; set; } = string.Empty;
    public string Condition     { get; set; } = string.Empty;
    public string? RefType      { get; set; }
    public string? RefId        { get; set; }
    public string? UserName     { get; set; }
}
