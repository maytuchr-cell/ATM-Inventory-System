using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api.Controllers;

/// <summary>
/// Registry of individual, serial-tracked units for parts that need piece-level tracking
/// (serial numbers, per-lot expiry, repairability). Quantities still live in PartStock;
/// PartUnit records the identity and lifecycle of specific physical pieces.
/// </summary>
[ApiController]
[Route("[controller]")]
public class PartUnitController : ControllerBase
{
    private readonly AppDbContext _context;

    public PartUnitController(AppDbContext context) => _context = context;

    // GET /api/PartUnit?search=&partNo=&partId=&status=&condition=&locationId=
    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] string? search,
        [FromQuery] string? partNo,
        [FromQuery] int? partId,
        [FromQuery] string? status,
        [FromQuery] string? condition,
        [FromQuery] int? locationId)
    {
        var q = _context.PartUnits.Include(u => u.Part).Include(u => u.Location).AsQueryable();

        if (partId.HasValue) q = q.Where(u => u.PartId == partId);
        if (!string.IsNullOrWhiteSpace(partNo)) q = q.Where(u => u.Part != null && u.Part.PartNo == partNo);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(u => u.Status == status);
        if (!string.IsNullOrWhiteSpace(condition)) q = q.Where(u => u.Condition == condition);
        if (locationId.HasValue) q = q.Where(u => u.LocationId == locationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(u => u.SerialNo.ToLower().Contains(s)
                || (u.Part != null && (u.Part.PartNo.ToLower().Contains(s) || u.Part.PartName.ToLower().Contains(s)))
                || (u.Location != null && u.Location.Name.ToLower().Contains(s)));
        }

        var rawUnits = q.OrderBy(u => u.SerialNo).Select(u => new {
            u.Id, u.PartId, partNo = u.Part!.PartNo, partName = u.Part.PartName,
            u.LocationId, location = u.Location == null ? null : u.Location.Name,
            u.SerialNo, u.Condition, u.ExpiryDate, u.IsUnrepairable, u.ReceivedAt, u.Status
        }).ToList();

        var issuedSerials = rawUnits
            .Where(u => u.Status == "Issued" && !string.IsNullOrWhiteSpace(u.SerialNo))
            .Select(u => u.SerialNo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var holders = new Dictionary<string, (string TechName, string? CaseNo, int? TicketId)>(StringComparer.OrdinalIgnoreCase);
        if (issuedSerials.Count > 0)
        {
            // 1. Check Tickets & TicketPartLines
            var ticketLines = _context.TicketPartLines
                .Include(l => l.Ticket)
                .Where(l => l.SerialNo != null && issuedSerials.Contains(l.SerialNo))
                .ToList();
            foreach (var l in ticketLines)
            {
                if (!string.IsNullOrWhiteSpace(l.Ticket?.TechName))
                    holders[l.SerialNo!] = (l.Ticket.TechName, l.Ticket.ExternalTicketNo, l.TicketId);
            }

            // 2. Check StockMovements for baseline / Outbound remarks
            var unmapped = issuedSerials.Where(s => !holders.ContainsKey(s)).ToList();
            if (unmapped.Count > 0)
            {
                var movements = _context.StockMovements
                    .Where(m => m.SerialNo != null && unmapped.Contains(m.SerialNo))
                    .OrderBy(m => m.Timestamp)
                    .ToList();

                foreach (var m in movements)
                {
                    string? tech = null;
                    string? cNo = null;
                    if (!string.IsNullOrWhiteSpace(m.Remarks) && m.Remarks.Contains("ช่าง:"))
                    {
                        var parts = m.Remarks.Split('|');
                        foreach (var p in parts)
                        {
                            var pt = p.Trim();
                            if (pt.StartsWith("ช่าง:")) tech = pt.Substring(5).Trim();
                            else if (pt.StartsWith("เคส:")) cNo = pt.Substring(4).Trim();
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(m.UserName) && m.UserName != "admin" && m.UserName != "System" && m.UserName != "DHL Daily Report (Auto)")
                    {
                        tech = m.UserName;
                    }

                    if (!string.IsNullOrWhiteSpace(tech))
                    {
                        holders[m.SerialNo!] = (tech, cNo, null);
                    }
                }
            }
        }

        var result = rawUnits.Select(u => {
            string? holder = null;
            string? caseNo = null;
            int? ticketId = null;
            if (u.Status == "Issued" && !string.IsNullOrWhiteSpace(u.SerialNo) && holders.TryGetValue(u.SerialNo, out var h))
            {
                holder = h.TechName;
                caseNo = h.CaseNo;
                ticketId = h.TicketId;
            }
            return new {
                u.Id, u.PartId, u.partNo, u.partName,
                u.LocationId, u.location,
                u.SerialNo, u.Condition, u.ExpiryDate, u.IsUnrepairable, u.ReceivedAt, u.Status,
                currentHolder = holder,
                caseNo = caseNo,
                ticketId = ticketId
            };
        });

        return Ok(result);
    }

    // POST /api/PartUnit
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost]
    public IActionResult Create([FromBody] PartUnitWriteDto dto)
    {
        var error = Validate(dto, null);
        if (error != null) return BadRequest(new { message = error });

        var unit = new PartUnit
        {
            PartId         = dto.PartId,
            LocationId     = dto.LocationId,
            SerialNo       = dto.SerialNo.Trim(),
            Condition      = string.IsNullOrWhiteSpace(dto.Condition) ? "Good" : dto.Condition,
            ExpiryDate     = dto.ExpiryDate,
            IsUnrepairable = dto.IsUnrepairable,
            ReceivedAt     = dto.ReceivedAt ?? DateTime.Now,
            Status         = string.IsNullOrWhiteSpace(dto.Status) ? "InStock" : dto.Status
        };
        _context.PartUnits.Add(unit);
        _context.SaveChanges();
        return Ok(unit);
    }

    // PUT /api/PartUnit/{id}
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] PartUnitWriteDto dto)
    {
        var unit = _context.PartUnits.FirstOrDefault(u => u.Id == id);
        if (unit == null) return NotFound();

        var error = Validate(dto, id);
        if (error != null) return BadRequest(new { message = error });

        unit.PartId         = dto.PartId;
        unit.LocationId     = dto.LocationId;
        unit.SerialNo       = dto.SerialNo.Trim();
        unit.Condition      = string.IsNullOrWhiteSpace(dto.Condition) ? "Good" : dto.Condition;
        unit.ExpiryDate     = dto.ExpiryDate;
        unit.IsUnrepairable = dto.IsUnrepairable;
        if (dto.ReceivedAt.HasValue) unit.ReceivedAt = dto.ReceivedAt.Value;
        unit.Status         = string.IsNullOrWhiteSpace(dto.Status) ? unit.Status : dto.Status;
        _context.SaveChanges();
        return Ok(unit);
    }

    // DELETE /api/PartUnit/{id}
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var unit = _context.PartUnits.FirstOrDefault(u => u.Id == id);
        if (unit == null) return NotFound();
        _context.PartUnits.Remove(unit);
        _context.SaveChanges();
        return Ok(new { message = "Part unit deleted." });
    }

    private string? Validate(PartUnitWriteDto dto, int? excludeId)
    {
        if (dto.PartId <= 0 || !_context.Parts.Any(p => p.Id == dto.PartId))
            return "A valid Part is required.";
        if (string.IsNullOrWhiteSpace(dto.SerialNo))
            return "Serial No. is required.";
        var serial = dto.SerialNo.Trim();
        if (_context.PartUnits.Any(u => u.SerialNo == serial && (!excludeId.HasValue || u.Id != excludeId.Value)))
            return $"Serial No. '{serial}' already exists.";
        if (dto.LocationId.HasValue && !_context.Locations.Any(l => l.Id == dto.LocationId.Value))
            return "Location not found.";
        return null;
    }
}

public class PartUnitWriteDto
{
    public int PartId { get; set; }
    public int? LocationId { get; set; }
    public string SerialNo { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsUnrepairable { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? Status { get; set; }
}
