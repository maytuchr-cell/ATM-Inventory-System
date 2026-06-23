using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GoodsReceiptController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;

    public GoodsReceiptController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // GET /api/GoodsReceipt?source=&vendorId=&from=&to=
    [HttpGet]
    public IActionResult GetAll(string? source, int? vendorId, DateTime? from, DateTime? to)
    {
        var query = _context.GoodsReceipts
            .Include(g => g.Lines)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(g => g.Source == source);
        if (vendorId.HasValue) query = query.Where(g => g.VendorId == vendorId);
        if (from.HasValue) query = query.Where(g => g.ReceivedAt >= from);
        if (to.HasValue) query = query.Where(g => g.ReceivedAt <= to);

        var receipts = query.OrderByDescending(g => g.ReceivedAt).ToList();

        var vendorMap = _context.Vendors.ToDictionary(v => v.Id, v => v.Name);
        var locMap    = _context.Locations.ToDictionary(l => l.Id, l => l.Name);
        var partMap   = _context.Parts.ToDictionary(p => p.PartNo, p => p.PartName);

        var result = receipts.Select(g => new
        {
            g.Id, g.ReceiptNo, g.Source, g.VendorId, g.RefDocument, g.LocationId,
            g.ReceivedBy, g.ReceivedAt, g.HandlingCost,
            vendorName   = g.VendorId.HasValue && vendorMap.ContainsKey(g.VendorId.Value) ? vendorMap[g.VendorId.Value] : null,
            locationName = locMap.ContainsKey(g.LocationId) ? locMap[g.LocationId] : null,
            lines = g.Lines.Select(l => new
            {
                l.Id, l.PartNo, l.Qty, l.Condition, l.SerialNo, l.IsManualAdjust, l.Remarks,
                partName = partMap.ContainsKey(l.PartNo) ? partMap[l.PartNo] : l.PartNo
            })
        });

        return Ok(result);
    }

    // POST /api/GoodsReceipt
    [HttpPost]
    public IActionResult Create([FromBody] GoodsReceiptCreateDto dto)
    {
        if (dto.Lines == null || dto.Lines.Count == 0)
            return BadRequest(new { message = "At least one line item is required." });

        var location = _context.Locations.FirstOrDefault(l => l.Id == dto.LocationId);
        if (location == null) return BadRequest(new { message = "Target location not found." });

        if (dto.Source == "LocalVendor" && dto.VendorId == null)
            return BadRequest(new { message = "VendorId is required for LocalVendor source." });

        var receipt = new GoodsReceipt
        {
            ReceiptNo    = $"GR-{DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)}",
            Source       = dto.Source,
            VendorId     = dto.VendorId,
            RefDocument  = dto.RefDocument,
            LocationId   = dto.LocationId,
            ReceivedBy   = dto.ReceivedBy ?? "Unknown",
            ReceivedAt   = DateTime.Now,
            HandlingCost = dto.HandlingCost
        };

        var autoCreated = new List<string>();

        foreach (var line in dto.Lines)
        {
            if (line.Qty <= 0)
                return BadRequest(new { message = $"Qty for {line.PartNo} must be greater than zero." });
            if (line.IsManualAdjust && string.IsNullOrWhiteSpace(line.Remarks))
                return BadRequest(new { message = $"Remarks are required for manual adjustment on {line.PartNo}." });

            var part = _context.Parts.FirstOrDefault(p => p.PartNo == line.PartNo);
            if (part == null)
            {
                // Auto-create part from GR data (use Remarks as PartName if provided)
                part = new Part
                {
                    PartNo        = line.PartNo,
                    PartName      = !string.IsNullOrWhiteSpace(line.Remarks) ? line.Remarks : line.PartNo,
                    Unit          = "pcs",
                    StockQuantity = 0,
                    MinStock      = 1,
                    MaxStock      = 100,
                    ReorderPoint  = 3,
                    IsActive      = true,
                };
                _context.Parts.Add(part);
                _context.SaveChanges();
                autoCreated.Add(line.PartNo);
            }
            else if (!part.IsActive)
            {
                part.IsActive = true;
            }

            receipt.Lines.Add(new GoodsReceiptLine
            {
                PartNo         = line.PartNo,
                Qty            = line.Qty,
                Condition      = line.Condition,
                SerialNo       = line.SerialNo,
                IsManualAdjust = line.IsManualAdjust,
                Remarks        = line.Remarks
            });
        }

        _context.GoodsReceipts.Add(receipt);
        _context.SaveChanges(); // assigns receipt.Id, line IDs

        foreach (var line in receipt.Lines)
        {
            _stock.AdjustStock(
                partNo: line.PartNo, locationId: dto.LocationId, qtyDelta: line.Qty,
                condition: line.Condition, movementType: "GR", refType: "GoodsReceipt",
                refId: receipt.Id.ToString(), userName: receipt.ReceivedBy,
                remarks: line.Remarks, cost: dto.HandlingCost, serialNo: line.SerialNo);
        }
        _context.SaveChanges();

        return Ok(new {
            message = "Goods receipt recorded.",
            receiptId = receipt.Id,
            receiptNo = receipt.ReceiptNo,
            autoCreatedParts = autoCreated,
            autoCreatedCount = autoCreated.Count
        });
    }

    // POST /api/GoodsReceipt/import-csv  (FR1-03)
    // Accepts a CSV file with columns: PartNo,SerialNo,Qty,Condition
    // Creates one GoodsReceipt with a line per CSV row.
    [HttpPost("import-csv")]
    public async Task<IActionResult> ImportCsv(
        IFormFile file,
        [FromForm] int locationId,
        [FromForm] string? source,
        [FromForm] string? receivedBy,
        [FromForm] decimal? handlingCost)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".csv")
            return BadRequest(new { message = "Only .csv files are supported." });

        var location = _context.Locations.FirstOrDefault(l => l.Id == locationId);
        if (location == null) return BadRequest(new { message = "Target location not found." });

        var lines = new List<GoodsReceiptLine>();
        var errors = new List<string>();

        using var reader = new StreamReader(file.OpenReadStream());
        var allLines = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.None);
        if (allLines.Length == 0) return BadRequest(new { message = "CSV is empty." });

        int rowNum = 1; // skip header (index 0)
        foreach (var rawRow in allLines.Skip(1))
        {
            rowNum++;
            var row = rawRow?.Trim();
            if (string.IsNullOrEmpty(row)) continue;

            var cols = row.Split(',');
            if (cols.Length < 3) { errors.Add($"Row {rowNum}: too few columns."); continue; }

            var partNo    = cols[0].Trim().Trim('"');
            var serialNo  = cols.Length > 1 ? cols[1].Trim().Trim('"') : null;
            var qtyStr    = cols[2].Trim().Trim('"');
            var condition = cols.Length > 3 ? cols[3].Trim().Trim('"') : "Good";

            if (!int.TryParse(qtyStr, out int qty) || qty <= 0)
            { errors.Add($"Row {rowNum}: invalid Qty '{qtyStr}'."); continue; }

            if (condition != "Good" && condition != "Defective") condition = "Good";

            var part = _context.Parts.FirstOrDefault(p => p.PartNo == partNo && p.IsActive);
            if (part == null) { errors.Add($"Row {rowNum}: Part '{partNo}' not found."); continue; }

            lines.Add(new GoodsReceiptLine
            {
                PartNo    = partNo,
                SerialNo  = string.IsNullOrEmpty(serialNo) ? null : serialNo,
                Qty       = qty,
                Condition = condition
            });
        }

        if (errors.Any() && !lines.Any())
            return BadRequest(new { message = "CSV parsing failed.", errors });

        var receipt = new GoodsReceipt
        {
            ReceiptNo    = $"GR-{DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)}-CSV",
            Source       = source ?? "LocalVendor",
            LocationId   = locationId,
            ReceivedBy   = receivedBy ?? "Import",
            ReceivedAt   = DateTime.Now,
            HandlingCost = handlingCost
        };

        receipt.Lines = lines;
        _context.GoodsReceipts.Add(receipt);
        _context.SaveChanges();

        foreach (var line in receipt.Lines)
        {
            _stock.AdjustStock(
                partNo: line.PartNo, locationId: locationId, qtyDelta: line.Qty,
                condition: line.Condition, movementType: "GR", refType: "GoodsReceipt",
                refId: receipt.Id.ToString(), userName: receipt.ReceivedBy,
                serialNo: line.SerialNo);
        }
        _context.SaveChanges();

        return Ok(new
        {
            message  = $"Imported {lines.Count} lines.",
            receiptId = receipt.Id,
            receiptNo = receipt.ReceiptNo,
            warnings  = errors
        });
    }
}

public class GoodsReceiptCreateDto
{
    public string Source { get; set; } = string.Empty; // GRG | LocalVendor
    public int? VendorId { get; set; }
    public string? RefDocument { get; set; }
    public int LocationId { get; set; }
    public string? ReceivedBy { get; set; }
    public decimal? HandlingCost { get; set; }
    public List<GoodsReceiptLineDto> Lines { get; set; } = new();
}

public class GoodsReceiptLineDto
{
    public string PartNo { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string Condition { get; set; } = "Good";
    public string? SerialNo { get; set; }
    public bool IsManualAdjust { get; set; }
    public string? Remarks { get; set; }
}
