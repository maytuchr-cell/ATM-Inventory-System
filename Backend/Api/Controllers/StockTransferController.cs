using Microsoft.AspNetCore.Mvc;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StockTransferController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;

    public StockTransferController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // GET /api/StockTransfer?status=
    [HttpGet]
    public IActionResult GetAll(string? status)
    {
        var query = _context.StockTransfers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);

        var transfers = query.OrderByDescending(s => s.CreatedAt).ToList();
        var partMap = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);
        var locMap  = _context.Locations.ToDictionary(l => l.Id, l => l.Name);

        var result = transfers.Select(s => new
        {
            s.Id, s.PartNo, s.Qty, s.Condition, s.FromLocationId, s.ToLocationId, s.Status,
            s.RequestedBy, s.ApprovedBy, s.CreatedAt, s.ApprovedAt, s.ConfirmedAt, s.ReceivedAt,
            partName     = partMap.GetValueOrDefault(s.PartNo, s.PartNo),
            fromLocation = locMap.GetValueOrDefault(s.FromLocationId, "—"),
            toLocation   = locMap.GetValueOrDefault(s.ToLocationId, "—"),
        });

        return Ok(result);
    }

    // POST /api/StockTransfer
    [HttpPost]
    public IActionResult Create([FromBody] TransferCreateDto dto)
    {
        if (dto.FromLocationId == dto.ToLocationId)
            return BadRequest(new { message = "Source and destination locations must differ." });
        if (dto.Qty <= 0)
            return BadRequest(new { message = "Qty must be greater than zero." });

        var part = _context.Parts.FirstOrDefault(p => p.PartNo == dto.PartNo && p.IsActive);
        if (part == null) return BadRequest(new { message = "Part not found or inactive." });

        var transfer = new StockTransfer
        {
            PartNo         = dto.PartNo,
            Qty            = dto.Qty,
            Condition      = dto.Condition,
            FromLocationId = dto.FromLocationId,
            ToLocationId   = dto.ToLocationId,
            Status         = "Pending",
            RequestedBy    = dto.RequestedBy ?? "Unknown",
            CreatedAt      = DateTime.Now
        };

        _context.StockTransfers.Add(transfer);
        _context.SaveChanges();
        return Ok(new { message = "Transfer request created.", transferId = transfer.Id });
    }

    // PUT /api/StockTransfer/{id}/approve
    [HttpPut("{id}/approve")]
    public IActionResult Approve(int id, [FromBody] TransferActionDto dto)
    {
        var transfer = _context.StockTransfers.FirstOrDefault(s => s.Id == id);
        if (transfer == null) return NotFound(new { message = "Transfer not found." });
        if (transfer.Status != "Pending") return BadRequest(new { message = "Only pending transfers can be approved." });

        transfer.Status     = "Approved";
        transfer.ApprovedBy = dto.UserName ?? "Unknown";
        transfer.ApprovedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Transfer approved.", transfer });
    }

    // PUT /api/StockTransfer/{id}/confirm-send
    [HttpPut("{id}/confirm-send")]
    public IActionResult ConfirmSend(int id, [FromBody] TransferActionDto dto)
    {
        var transfer = _context.StockTransfers.FirstOrDefault(s => s.Id == id);
        if (transfer == null) return NotFound(new { message = "Transfer not found." });
        if (transfer.Status != "Approved") return BadRequest(new { message = "Only approved transfers can be sent." });

        try
        {
            // Stock leaves FromLocation and is recorded as "in transit" by the movement record itself —
            // it does not land in ToLocation until /receive is called.
            _stock.AdjustStock(
                partNo: transfer.PartNo, locationId: transfer.FromLocationId, qtyDelta: -transfer.Qty,
                condition: transfer.Condition, movementType: "Transfer", refType: "StockTransfer",
                refId: id.ToString(), userName: dto.UserName ?? "Unknown",
                remarks: $"Sent — in transit to location {transfer.ToLocationId}");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        transfer.Status      = "InTransit";
        transfer.ConfirmedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Transfer confirmed — stock is now in transit.", transfer });
    }

    // PUT /api/StockTransfer/{id}/receive
    [HttpPut("{id}/receive")]
    public IActionResult Receive(int id, [FromBody] TransferActionDto dto)
    {
        var transfer = _context.StockTransfers.FirstOrDefault(s => s.Id == id);
        if (transfer == null) return NotFound(new { message = "Transfer not found." });
        if (transfer.Status != "InTransit") return BadRequest(new { message = "Only in-transit transfers can be received." });

        _stock.AdjustStock(
            partNo: transfer.PartNo, locationId: transfer.ToLocationId, qtyDelta: transfer.Qty,
            condition: transfer.Condition, movementType: "Transfer", refType: "StockTransfer",
            refId: id.ToString(), userName: dto.UserName ?? "Unknown",
            remarks: $"Received from location {transfer.FromLocationId}");

        transfer.Status     = "Received";
        transfer.ReceivedAt = DateTime.Now;
        _context.SaveChanges();
        return Ok(new { message = "Transfer received.", transfer });
    }
}

public class TransferCreateDto
{
    public string PartNo { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string Condition { get; set; } = "Good";
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public string? RequestedBy { get; set; }
}

public class TransferActionDto
{
    public string? UserName { get; set; }
}
