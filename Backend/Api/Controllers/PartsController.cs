using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PartsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly CatalogImportService _catalogImport;

    public PartsController(AppDbContext context, StockService stock, IConfiguration config, IWebHostEnvironment env, CatalogImportService catalogImport)
    {
        _context = context;
        _stock = stock;
        _config = config;
        _env = env;
        _catalogImport = catalogImport;
    }

    // POST /Parts/import/preview — parses a GRG-style catalog .xlsx and returns what an import
    // would do (new/update/error per row) without writing anything. See CatalogImportService.
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost("import/preview")]
    [RequestSizeLimit(200_000_000)]
    public IActionResult ImportPreview(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported." });
        try
        {
            using var ms = new MemoryStream();
            file.OpenReadStream().CopyTo(ms);
            var result = _catalogImport.PreviewParts(ms.ToArray());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST /Parts/import/confirm — commits the same file (new parts inserted, existing ones
    // updated only if overwriteExisting=true), then immediately runs the "Same part no." ->
    // EquivalentGroup linking pass on the same bytes, so newly-created parts are eligible too —
    // one upload does both. See CatalogImportService.
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost("import/confirm")]
    [RequestSizeLimit(200_000_000)]
    public IActionResult ImportConfirm(IFormFile file, [FromForm] string? project, [FromForm] bool overwriteExisting = false)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported." });
        try
        {
            using var ms = new MemoryStream();
            file.OpenReadStream().CopyTo(ms);
            var bytes = ms.ToArray();
            var addedBy = User?.Identity?.Name ?? "admin";

            var partsResult = _catalogImport.ConfirmParts(bytes, project, overwriteExisting, addedBy);
            var linkResult = _catalogImport.LinkEquivalents(bytes, file.FileName);

            return Ok(new
            {
                message = "Import complete.",
                parts = new { partsResult.Inserted, partsResult.Updated, partsResult.Skipped, partsResult.ErrorCount },
                equivalents = new
                {
                    linkResult.RowsProcessed, linkResult.GroupsCreated, linkResult.GroupsMerged,
                    linkResult.MembersAdded, linkResult.NotFoundCount, linkResult.NotFoundSample
                }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Dictionary<int, List<object>> ImagesByPart(IEnumerable<int> partIds)
    {
        var ids = partIds.ToList();
        return _context.PartImages
            .Where(i => ids.Contains(i.PartId))
            .OrderBy(i => i.SortOrder).ThenBy(i => i.PartImageId)
            .GroupBy(i => i.PartId)
            .ToDictionary(g => g.Key, g => g.Select(i => (object)new {
                i.PartImageId, i.FilePath, i.FileName, i.SortOrder
            }).ToList());
    }

    // On-hand total for each part = SUM(PartStock.GoodQty). Computed from PartStock,
    // never stored on Part. Pass the part ids you need totals for.
    private Dictionary<int, int> StockTotals(IEnumerable<int> partIds)
    {
        var ids = partIds.ToList();
        return _context.PartStocks
            .Where(s => ids.Contains(s.PartId))
            .GroupBy(s => s.PartId)
            .Select(g => new { PartId = g.Key, Good = g.Sum(x => x.GoodQty) })
            .ToDictionary(x => x.PartId, x => x.Good);
    }

    // Per-location breakdown for the two buckets Admin actually cares about on the Parts Master
    // list: the central warehouse (what a new withdraw can actually draw from — see
    // TicketController.ApproveTicket) vs. stock already issued out to technicians. The total
    // (StockTotals above) can be higher than what's approvable because it includes tech-held
    // stock, which is exactly the "shows 2 but withdraw says short by 1" confusion this fixes.
    private (Dictionary<int, int> Warehouse, Dictionary<int, int> Tech) StockByBucket(IEnumerable<int> partIds)
    {
        var ids = partIds.ToList();
        var whId = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT")?.Id;
        var techId = _context.Locations.FirstOrDefault(l => l.LocationType == "OL_TECHNICIAN")?.Id;

        var rows = _context.PartStocks
            .Where(s => ids.Contains(s.PartId) && (s.LocationId == whId || s.LocationId == techId))
            .ToList();

        var wh = rows.Where(s => s.LocationId == whId).ToDictionary(s => s.PartId, s => s.GoodQty);
        var tech = rows.Where(s => s.LocationId == techId).ToDictionary(s => s.PartId, s => s.GoodQty);
        return (wh, tech);
    }

    // GET /api/Parts?categoryId=&isActive=&search=
    [HttpGet]
    public IActionResult GetAll([FromQuery] int? categoryId, [FromQuery] bool? isActive, [FromQuery] string? search)
    {
        var q = _context.Parts.Include(p => p.Category).AsQueryable();
        if (categoryId.HasValue) q = q.Where(p => p.CategoryId == categoryId);
        if (isActive.HasValue)   q = q.Where(p => p.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.PartName.Contains(search) || p.PartNo.Contains(search));

        var parts = q.OrderBy(p => p.PartNo).ToList();

        // on-hand totals from PartStock (source of truth)
        var stockTotals = StockTotals(parts.Select(p => p.Id));
        var (whStock, techStock) = StockByBucket(parts.Select(p => p.Id));
        var images = ImagesByPart(parts.Select(p => p.Id));

        // attach known serial numbers from StockMovements
        var serialMap = _context.StockMovements
            .Where(m => m.SerialNo != null && m.SerialNo != "")
            .GroupBy(m => m.PartNo)
            .ToDictionary(g => g.Key, g => g.Select(m => m.SerialNo!).Distinct().ToList());

        var result = parts.Select(p => new {
            p.Id, p.PartNo, p.PartName, p.OrderNumber, p.Unit,
            StockQuantity = stockTotals.GetValueOrDefault(p.Id, 0),
            WarehouseStock = whStock.GetValueOrDefault(p.Id, 0),
            TechStock = techStock.GetValueOrDefault(p.Id, 0),
            p.CategoryId, p.MinStock, p.MaxStock,
            p.ReorderPoint, p.CostPerUnit, p.CatalogueRef, p.SerialNo,
            p.MainUnit, p.Remark, p.ImagePath, p.Zone, p.DeviceType, p.AddedBy, p.Lot, p.Project, p.AddedDate,
            p.IsActive, p.CreatedAt, p.UpdatedAt,
            category = p.Category == null ? null : new { p.Category.Id, p.Category.Name },
            serialNos = serialMap.ContainsKey(p.PartNo) ? serialMap[p.PartNo] : new List<string>(),
            images = images.GetValueOrDefault(p.Id, new List<object>())
        });

        return Ok(result);
    }

    // GET /api/Parts/{id}
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var part = _context.Parts.Include(p => p.Category).FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        // on-hand total + per-location breakdown, computed from PartStock
        var byLocation = _context.PartStocks
            .Where(s => s.PartId == id)
            .Include(s => s.Location)
            .Select(s => new {
                locationId = s.LocationId,
                location = s.Location == null ? null : s.Location.Name,
                s.GoodQty, s.BadQty
            })
            .ToList();

        var images = _context.PartImages.Where(i => i.PartId == id)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.PartImageId)
            .Select(i => new { i.PartImageId, i.FilePath, i.FileName, i.SortOrder })
            .ToList();

        return Ok(new {
            part.Id, part.PartNo, part.PartName, part.OrderNumber, part.Unit, part.SerialNo,
            StockQuantity = byLocation.Sum(s => s.GoodQty),
            part.CategoryId, part.MinStock, part.MaxStock, part.ReorderPoint,
            part.CostPerUnit, part.CatalogueRef, part.MainUnit, part.Remark, part.ImagePath, part.Zone, part.DeviceType, part.AddedBy, part.Lot, part.Project, part.AddedDate,
            part.IsActive, part.CreatedAt, part.UpdatedAt,
            category = part.Category == null ? null : new { part.Category.Id, part.Category.Name },
            stockByLocation = byLocation,
            images
        });
    }

    // POST /api/Parts/{id}/images — upload one photo to this part's gallery.
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost("{id}/images")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });
        if (file.Length > 10_000_000)
            return BadRequest(new { message = "File too large (max 10MB)." });

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext))
            return BadRequest(new { message = "Only image files are allowed." });

        var assetRoot = _config["AssetPath"] ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploads = Path.Combine(assetRoot, "parts");
        Directory.CreateDirectory(uploads);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploads, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var nextSort = _context.PartImages.Where(i => i.PartId == id).Select(i => (int?)i.SortOrder).Max() ?? -1;
        var image = new PartImage
        {
            PartId = id,
            FilePath = $"/assets/parts/{fileName}",
            FileName = file.FileName,
            SortOrder = nextSort + 1
        };
        _context.PartImages.Add(image);
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "IMAGE_ADD", null, new { image.PartImageId, image.FilePath });
        return Ok(new { image.PartImageId, image.FilePath, image.FileName, image.SortOrder });
    }

    // DELETE /api/Parts/{id}/images/{imageId}
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpDelete("{id}/images/{imageId}")]
    public IActionResult DeleteImage(int id, int imageId)
    {
        var image = _context.PartImages.FirstOrDefault(i => i.PartImageId == imageId && i.PartId == id);
        if (image == null) return NotFound();

        _context.PartImages.Remove(image);
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "IMAGE_DELETE", new { image.PartImageId, image.FilePath }.ToString(), null);
        return Ok(new { message = "Image removed." });
    }

    // GET /api/Parts/{id}/holders — which technician(s) currently have this part checked out,
    // and how many. There's no per-tech stock location (OL_TECHNICIAN is one shared bucket), so
    // this is derived from open Tickets instead: a ticket is "holding" a part once the tech has
    // received it (reached เบิก) and until the return leg fully closes.
    //
    // Return lines do NOT reduce the outstanding qty here, even once the tech has submitted a
    // return (status รอ/อนุมัติคืน/เดินทาง on the return leg) — ConfirmReturnArrived is the only
    // place PartStock actually moves the part out of techLoc, and it does that atomically in the
    // same call that flips Status to คืน. Subtracting Return-line qty before that point would
    // undercount against the real techStock number, which stays unchanged until confirm-return.
    [HttpGet("{id}/holders")]
    public IActionResult GetHolders(int id)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        // A withdraw batch counts as "holding" the part once the tech has physically received it
        // (Status == "เบิก") — that stays true regardless of whether a return has since started
        // against the Ticket (unlike the withdraw leg used to, a batch's own Status doesn't shift
        // once received; the return leg is tracked separately on Ticket).
        var lines = _context.TicketPartLines
            .Where(l => l.PartNo == part.PartNo && l.LineType == "Withdraw" && l.WithdrawBatchId != null)
            .ToList();
        var batchIds = lines.Select(l => l.WithdrawBatchId!.Value).Distinct().ToList();
        var batches = _context.WithdrawBatches.Include(b => b.Ticket).Where(b => batchIds.Contains(b.WithdrawBatchId)).ToList();

        var holders = new List<object>();
        foreach (var batch in batches)
        {
            if (batch.Status != "เบิก") continue;

            var qty = lines.Where(l => l.WithdrawBatchId == batch.WithdrawBatchId).Sum(l => l.Quantity);
            if (qty <= 0) continue;

            holders.Add(new
            {
                TicketId = batch.TicketId, ExternalTicketNo = batch.Ticket?.ExternalTicketNo,
                TechName = batch.Ticket?.TechName, TechDept = batch.Ticket?.TechDept,
                Status = batch.Status, Quantity = qty
            });
        }

        return Ok(holders);
    }

    // POST /api/Parts
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPost]
    public IActionResult Create([FromBody] PartWriteDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });

        if (_context.Parts.Any(p => p.PartNo == dto.PartNo))
            return BadRequest(new { message = $"PartNo '{dto.PartNo}' already exists." });

        var part = MapFromDto(new Part(), dto);
        part.CreatedAt = DateTime.UtcNow;
        part.UpdatedAt = DateTime.UtcNow;
        _context.Parts.Add(part);
        _context.SaveChanges();

        // Initial stock (if any) becomes a PartStock row at the main warehouse + an opening
        // StockMovement — the same path every other stock change takes. No stored total.
        if (dto.StockQuantity > 0)
        {
            var mainWh = _context.Locations.FirstOrDefault(l => l.Code == "WH-RAT")
                         ?? _context.Locations.FirstOrDefault();
            if (mainWh != null)
            {
                _stock.AdjustStock(part.PartNo, mainWh.Id, dto.StockQuantity, "Good",
                    "GR", "OpeningBalance", null, CurrentUser(), "Initial stock on part creation");
                _context.SaveChanges();
            }
        }

        WriteAudit("Part", part.Id.ToString(), "CREATE", null, part);
        return Ok(PartSummary(part));
    }

    // PUT /api/Parts/{id}
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] PartWriteDto dto)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });

        if (_context.Parts.Any(p => p.PartNo == dto.PartNo && p.Id != id))
            return BadRequest(new { message = $"PartNo '{dto.PartNo}' already used by another part." });

        var old = JsonSerializer.Serialize(part);
        MapFromDto(part, dto);
        part.UpdatedAt = DateTime.UtcNow;
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "UPDATE", old, part);
        return Ok(PartSummary(part));
    }

    // DELETE /api/Parts/{id}  — soft delete
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        var old = JsonSerializer.Serialize(part);
        part.IsActive = false;
        part.UpdatedAt = DateTime.UtcNow;
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "DELETE", old, part);
        return Ok(new { message = "Part deactivated." });
    }

    // PATCH /api/Parts/{id}/restore
    [Authorize(Policy = "CanWriteMasterData")]
    [HttpPatch("{id}/restore")]
    public IActionResult Restore(int id)
    {
        var part = _context.Parts.FirstOrDefault(p => p.Id == id);
        if (part == null) return NotFound();

        part.IsActive = true;
        part.UpdatedAt = DateTime.UtcNow;
        _context.SaveChanges();

        WriteAudit("Part", id.ToString(), "UPDATE", null, part);
        return Ok(new { message = "Part restored." });
    }

    // Returns an error message if the DTO is invalid, otherwise null.
    private static string? Validate(PartWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PartNo))
            return "Part No. is required.";
        if (string.IsNullOrWhiteSpace(dto.PartName))
            return "Part Name is required.";
        if (dto.MinStock < 0 || dto.MaxStock < 0 || dto.ReorderPoint < 0)
            return "Min / Max / Reorder values cannot be negative.";
        if (dto.MinStock > dto.MaxStock)
            return $"Min Stock ({dto.MinStock}) cannot be greater than Max Stock ({dto.MaxStock}).";
        if (dto.ReorderPoint > dto.MaxStock)
            return $"Reorder Point ({dto.ReorderPoint}) cannot be greater than Max Stock ({dto.MaxStock}).";
        if (dto.StockQuantity < 0)
            return "Stock Quantity cannot be negative.";
        if (dto.CostPerUnit.HasValue && dto.CostPerUnit < 0)
            return "Cost per unit cannot be negative.";
        return null;
    }

    private static Part MapFromDto(Part part, PartWriteDto dto)
    {
        part.PartNo       = dto.PartNo;
        part.PartName     = dto.PartName;
        part.OrderNumber  = dto.OrderNumber ?? string.Empty;
        part.Unit         = dto.Unit ?? "pcs";
        part.SerialNo     = dto.SerialNo;
        // On-hand stock lives in PartStock, not on Part — initial stock is handled in Create()
        // via StockService, and all later changes go through GR/Issue/Return/Transfer/Disposal.
        part.CategoryId   = dto.CategoryId;
        part.CatalogueRef = dto.CatalogueRef;
        part.MinStock     = dto.MinStock;
        part.MaxStock     = dto.MaxStock;
        part.ReorderPoint = dto.ReorderPoint;
        part.TrackingNumber = dto.TrackingNumber;
        part.Aging        = dto.Aging;
        part.CostPerUnit  = dto.CostPerUnit;
        part.ExpiryDate     = dto.ExpiryDate;
        part.IsUnrepairable = dto.IsUnrepairable;
        part.MainUnit       = dto.MainUnit;
        part.Remark         = dto.Remark;
        if (dto.ImagePath != null) part.ImagePath = dto.ImagePath;
        part.Zone           = dto.Zone;
        part.DeviceType     = dto.DeviceType;
        part.AddedBy        = dto.AddedBy;
        part.Lot            = dto.Lot;
        part.Project        = dto.Project;
        part.AddedDate      = dto.AddedDate ?? DateTime.Now;
        return part;
    }

    // Clean response shape for a single part (avoids serializing the Stocks navigation cycle).
    private object PartSummary(Part p) => new
    {
        p.Id, p.PartNo, p.PartName, p.OrderNumber, p.Unit, p.SerialNo,
        StockQuantity = _context.PartStocks.Where(s => s.PartId == p.Id).Sum(s => s.GoodQty),
        p.CategoryId, p.MinStock, p.MaxStock, p.ReorderPoint,
        p.CostPerUnit, p.CatalogueRef, p.MainUnit, p.Remark, p.ImagePath, p.Zone, p.DeviceType, p.AddedBy, p.Lot, p.Project, p.AddedDate,
        p.IsActive, p.CreatedAt, p.UpdatedAt,
        images = _context.PartImages.Where(i => i.PartId == p.Id)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.PartImageId)
            .Select(i => new { i.PartImageId, i.FilePath, i.FileName, i.SortOrder })
            .ToList()
    };

    private string CurrentUser() =>
        User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
        ?? User?.Identity?.Name
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? "system";

    private string CurrentUserId() =>
        User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? "system";

    private static readonly JsonSerializerOptions _auditJson = new()
    { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };

    private void WriteAudit(string entityType, string entityId, string action, string? oldValues, object? newValues)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId   = entityId,
            Action     = action,
            OldValues  = oldValues,
            NewValues  = newValues != null ? JsonSerializer.Serialize(newValues, _auditJson) : null,
            UserId     = CurrentUserId(),
            UserName   = CurrentUser(),
            Timestamp  = DateTime.UtcNow
        });
        _context.SaveChanges();
    }
}

public class PartWriteDto
{
    public string PartNo { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string? OrderNumber { get; set; }
    public string? Unit { get; set; }
    public string? SerialNo { get; set; }
    public int StockQuantity { get; set; }
    public int? CategoryId { get; set; }
    public string? CatalogueRef { get; set; }
    public int MinStock { get; set; } = 1;
    public int MaxStock { get; set; } = 100;
    public int ReorderPoint { get; set; } = 3;
    public string? TrackingNumber { get; set; }
    public int? Aging { get; set; }
    public decimal? CostPerUnit { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsUnrepairable { get; set; }
    public string? MainUnit { get; set; }
    public string? Remark { get; set; }
    public string? ImagePath { get; set; }
    public string? Zone { get; set; }
    public string? DeviceType { get; set; }
    public string? AddedBy { get; set; }
    public string? Lot { get; set; }
    public string? Project { get; set; }
    public DateTime? AddedDate { get; set; }
}
