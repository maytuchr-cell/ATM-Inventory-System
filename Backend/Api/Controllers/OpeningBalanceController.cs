using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

// Opening balance ("ยอดตั้งต้น") — sets one warehouse's stock to exactly what a full inventory
// audit file says is on the shelf, replacing whatever the system held before. This is the system's
// starting point for stock; afterwards stock only moves through recorded work (withdraw/approve/
// receive/return, and DHL Daily Report closing those jobs).
//
// Expected file: DHL's WMS audit export ("Dataone_Inventory Audit …xlsx"). Stock comes from the
// "4 Inven-All …" sheet (or any sheet with ITEM + ON_HAND_QTY + INVENTORY_STS headers); part →
// project comes from the "6.Part Project" sheet when present.
//
// The file is the truth for the chosen warehouse only:
//   - GOOD → GoodQty, BAD → RepairQty (same bucket the Daily Report compares DHL's BAD count to)
//   - parts at that warehouse that aren't in the file are set to 0
//   - every serial in the file is registered/moved into that warehouse (including units the system
//     thought were with a tech or at the repair center — decrementing those locations by one)
//   - serials the system has at that warehouse but the file doesn't mention are only reported,
//     not changed, so Admin can chase them individually
// Preview and Confirm run the exact same Plan(), so what Admin reviews is what gets written.
[ApiController]
[Route("GoodsReceipt/opening-balance")]
[Authorize(Policy = "SystemAdminOnly")]
public class OpeningBalanceController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;
    private readonly AuditService _audit;

    public OpeningBalanceController(AppDbContext context, StockService stock, AuditService audit)
    {
        _context = context;
        _stock = stock;
        _audit = audit;
    }

    private record FileRow(int Row, string PartNo, string PartName, int Qty, bool Bad, string? Serial, DateTime? ReceivedAt);

    private class PlanResult
    {
        public string SheetName = "";
        public List<string> Errors = new();
        public List<FileRow> Rows = new();
        public List<PartChange> Parts = new();
        public List<SerialChange> Serials = new();
        public List<object> SerialsNotInFile = new();
        public List<ProjectChange> Projects = new();
    }
    private record PartChange(int PartId, string PartNo, string PartName, int CurGood, int NewGood, int CurRepair, int NewRepair);
    private record SerialChange(string Serial, int PartId, string PartNo, bool Bad, DateTime? ReceivedAt,
                                int? UnitId, string Kind, string? FromStatus, int? FromLocationId, string? FromLocationCode);
    private record ProjectChange(int PartId, string PartNo, string? From, string To);

    private static string Norm(string? s) => (s ?? "").Trim();

    private static (IXLWorksheet ws, int headerRow, Dictionary<string, int> cols)? FindStockSheet(XLWorkbook wb)
    {
        var ordered = wb.Worksheets.OrderByDescending(w => w.Name.Contains("Inven-All", StringComparison.OrdinalIgnoreCase));
        foreach (var ws in ordered)
        {
            var lastCol = Math.Min(40, ws.LastColumnUsed()?.ColumnNumber() ?? 1);
            for (int r = 1; r <= 5; r++)
            {
                var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c <= lastCol; c++)
                {
                    var h = ws.Cell(r, c).GetString().Trim();
                    if (h.Length > 0 && !cols.ContainsKey(h)) cols[h] = c;
                }
                if (cols.ContainsKey("ITEM") && cols.ContainsKey("ON_HAND_QTY") && cols.ContainsKey("INVENTORY_STS"))
                    return (ws, r, cols);
            }
        }
        return null;
    }

    private static DateTime? CellDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        try
        {
            if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime();
            if (DateTime.TryParse(cell.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d)) return d;
        }
        catch { }
        return null;
    }

    private PlanResult Plan(Stream file, int locationId)
    {
        var plan = new PlanResult();
        using var wb = new XLWorkbook(file);

        var found = FindStockSheet(wb);
        if (found == null)
        {
            plan.Errors.Add("ไม่พบชีตที่มีคอลัมน์ ITEM, ON_HAND_QTY และ INVENTORY_STS — ต้องเป็นไฟล์ Inventory Audit ของ DHL");
            return plan;
        }
        var (ws, headerRow, cols) = found.Value;
        plan.SheetName = ws.Name;
        int C(string name) => cols.TryGetValue(name, out var c) ? c : -1;
        int cItem = C("ITEM"), cDesc = C("ITEM_DESC"), cQty = C("ON_HAND_QTY"), cSts = C("INVENTORY_STS"),
            cSn = C("SERIAL_NUMBER"), cRecv = C("RECEIVED_DATE");

        var partsByNo = _context.Parts.ToList().GroupBy(p => Norm(p.PartNo), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // ── Read + validate every row ──
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        var seenSerial = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var partNo = Norm(ws.Cell(r, cItem).GetString());
            if (partNo.Length == 0) continue;
            var qtyCell = ws.Cell(r, cQty);
            if (!qtyCell.TryGetValue<double>(out var qtyD) || qtyD < 0 || qtyD != Math.Floor(qtyD))
            { plan.Errors.Add($"แถว {r}: จำนวน '{qtyCell.GetString()}' ไม่ถูกต้อง"); continue; }
            var sts = Norm(ws.Cell(r, cSts).GetString()).ToUpperInvariant();
            if (sts != "GOOD" && sts != "BAD")
            { plan.Errors.Add($"แถว {r} ({partNo}): สถานะ '{sts}' ไม่รู้จัก (ต้องเป็น GOOD หรือ BAD)"); continue; }
            if (!partsByNo.ContainsKey(partNo))
            { plan.Errors.Add($"แถว {r}: ไม่พบรหัสอะไหล่ {partNo} ในทะเบียนอะไหล่ — เพิ่มใน Parts Master ก่อน"); continue; }

            var serial = cSn > 0 ? Norm(ws.Cell(r, cSn).GetString()) : "";
            var qty = (int)qtyD;
            if (serial.Length > 0)
            {
                if (qty != 1) plan.Errors.Add($"แถว {r} ({partNo}): S/N {serial} มีจำนวน {qty} — อะไหล่ที่มี S/N ต้องมี 1 ชิ้น");
                if (seenSerial.TryGetValue(serial, out var first)) plan.Errors.Add($"แถว {r} ({partNo}): S/N {serial} ซ้ำกับแถว {first}");
                else seenSerial[serial] = r;
            }
            plan.Rows.Add(new FileRow(r, partNo, cDesc > 0 ? Norm(ws.Cell(r, cDesc).GetString()) : "", qty, sts == "BAD",
                serial.Length > 0 ? serial : null, cRecv > 0 ? CellDate(ws.Cell(r, cRecv)) : null));
        }
        if (plan.Rows.Count == 0 && plan.Errors.Count == 0) plan.Errors.Add($"ชีต {ws.Name} ไม่มีข้อมูล");

        // ── Stock: file totals vs what the warehouse holds now ──
        var target = plan.Rows.GroupBy(x => partsByNo[x.PartNo].Id)
            .ToDictionary(g => g.Key, g => (good: g.Where(x => !x.Bad).Sum(x => x.Qty), repair: g.Where(x => x.Bad).Sum(x => x.Qty)));
        var current = _context.PartStocks.Where(s => s.LocationId == locationId).ToList()
            .ToDictionary(s => s.PartId, s => (good: s.GoodQty, repair: s.RepairQty));
        var partById = partsByNo.Values.ToDictionary(p => p.Id);
        foreach (var pid in target.Keys.Union(current.Keys))
        {
            var cur = current.GetValueOrDefault(pid);
            var tgt = target.GetValueOrDefault(pid);
            if (cur.good == tgt.good && cur.repair == tgt.repair && !target.ContainsKey(pid)) continue;
            var p = partById.GetValueOrDefault(pid);
            if (p == null) continue;
            plan.Parts.Add(new PartChange(pid, p.PartNo, p.PartName, cur.good, tgt.good, cur.repair, tgt.repair));
        }
        plan.Parts = plan.Parts.OrderByDescending(x => Math.Abs(x.NewGood - x.CurGood) + Math.Abs(x.NewRepair - x.CurRepair)).ToList();

        // ── Serials: register or move every serial in the file into this warehouse ──
        var fileSerials = plan.Rows.Where(x => x.Serial != null).ToList();
        var serialSet = fileSerials.Select(x => x.Serial!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var units = _context.PartUnits.ToList();
        var unitBySerial = units.GroupBy(u => Norm(u.SerialNo), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var locCode = _context.Locations.ToDictionary(l => l.Id, l => l.Code);
        foreach (var x in fileSerials)
        {
            var pid = partsByNo[x.PartNo].Id;
            if (!unitBySerial.TryGetValue(x.Serial!, out var u))
            {
                plan.Serials.Add(new SerialChange(x.Serial!, pid, x.PartNo, x.Bad, x.ReceivedAt, null, "new", null, null, null));
                continue;
            }
            var here = u.LocationId == locationId;
            var kind = here ? "here" : u.Status == "Issued" ? "fromTech" : "fromOther";
            plan.Serials.Add(new SerialChange(x.Serial!, pid, x.PartNo, x.Bad, x.ReceivedAt, u.Id, kind, u.Status,
                u.LocationId, u.LocationId.HasValue ? locCode.GetValueOrDefault(u.LocationId.Value) : null));
        }
        plan.SerialsNotInFile = units
            .Where(u => u.LocationId == locationId && (u.Status == "InStock" || u.Status == "InRepair")
                        && !serialSet.Contains(Norm(u.SerialNo)))
            .Select(u => (object)new { serial = u.SerialNo, partNo = partById.GetValueOrDefault(u.PartId)?.PartNo, status = u.Status })
            .ToList();

        // ── Projects from the "Part Project" sheet (ITEM + first "Project" column) ──
        var projWs = wb.Worksheets.FirstOrDefault(w => w.Name.Contains("Part Project", StringComparison.OrdinalIgnoreCase));
        if (projWs != null)
        {
            int pItem = -1, pProj = -1;
            var lastCol = Math.Min(20, projWs.LastColumnUsed()?.ColumnNumber() ?? 1);
            for (int c = 1; c <= lastCol; c++)
            {
                var h = projWs.Cell(1, c).GetString().Trim();
                if (pItem < 0 && h.Equals("ITEM", StringComparison.OrdinalIgnoreCase)) pItem = c;
                if (pProj < 0 && h.Equals("Project", StringComparison.OrdinalIgnoreCase)) pProj = c;
            }
            if (pItem > 0 && pProj > 0)
            {
                var projLast = projWs.LastRowUsed()?.RowNumber() ?? 1;
                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int r = 2; r <= projLast; r++)
                {
                    var pn = Norm(projWs.Cell(r, pItem).GetString());
                    var proj = Norm(projWs.Cell(r, pProj).GetString());
                    if (pn.Length == 0 || proj.Length == 0 || !done.Add(pn)) continue;
                    if (partsByNo.TryGetValue(pn, out var part) && !string.Equals(Norm(part.Project), proj, StringComparison.Ordinal))
                        plan.Projects.Add(new ProjectChange(part.Id, part.PartNo, part.Project, proj));
                }
            }
        }
        return plan;
    }

    private object Summary(PlanResult plan) => new
    {
        sheet = plan.SheetName,
        errors = plan.Errors.Take(200),
        errorCount = plan.Errors.Count,
        fileRows = plan.Rows.Count,
        fileGoodQty = plan.Rows.Where(x => !x.Bad).Sum(x => x.Qty),
        fileRepairQty = plan.Rows.Where(x => x.Bad).Sum(x => x.Qty),
        currentGoodQty = plan.Parts.Sum(x => x.CurGood),
        currentRepairQty = plan.Parts.Sum(x => x.CurRepair),
        parts = plan.Parts.Select(x => new { x.PartNo, x.PartName, curGood = x.CurGood, newGood = x.NewGood, curRepair = x.CurRepair, newRepair = x.NewRepair }),
        partsChanged = plan.Parts.Count(x => x.CurGood != x.NewGood || x.CurRepair != x.NewRepair),
        partsZeroed = plan.Parts.Count(x => x.NewGood == 0 && x.NewRepair == 0 && (x.CurGood != 0 || x.CurRepair != 0)),
        serials = new
        {
            total = plan.Serials.Count,
            newCount = plan.Serials.Count(s => s.Kind == "new"),
            alreadyHere = plan.Serials.Count(s => s.Kind == "here"),
            fromTech = plan.Serials.Where(s => s.Kind == "fromTech").Select(s => new { s.Serial, s.PartNo, from = s.FromLocationCode }),
            fromOther = plan.Serials.Where(s => s.Kind == "fromOther").Select(s => new { s.Serial, s.PartNo, from = s.FromLocationCode, status = s.FromStatus }),
            notInFile = plan.SerialsNotInFile,
        },
        projects = plan.Projects.Select(p => new { p.PartNo, from = p.From, to = p.To }),
    };

    // POST /GoodsReceipt/opening-balance/preview — read-only.
    [HttpPost("preview")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult Preview(IFormFile file, [FromForm] int locationId)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "กรุณาแนบไฟล์" });
        if (!_context.Locations.Any(l => l.Id == locationId)) return BadRequest(new { message = "ไม่พบคลังปลายทาง" });
        try
        {
            using var s = file.OpenReadStream();
            return Ok(Summary(Plan(s, locationId)));
        }
        catch (Exception ex) { return BadRequest(new { message = $"อ่านไฟล์ไม่สำเร็จ: {ex.Message}" }); }
    }

    // POST /GoodsReceipt/opening-balance/confirm — applies the plan in one transaction.
    [HttpPost("confirm")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult Confirm(IFormFile file, [FromForm] int locationId, [FromForm] string? receivedBy)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "กรุณาแนบไฟล์" });
        if (string.IsNullOrWhiteSpace(receivedBy)) return BadRequest(new { message = "กรุณากรอกชื่อผู้รับเข้า" });
        var location = _context.Locations.FirstOrDefault(l => l.Id == locationId);
        if (location == null) return BadRequest(new { message = "ไม่พบคลังปลายทาง" });

        PlanResult plan;
        using (var s = file.OpenReadStream()) plan = Plan(s, locationId);
        if (plan.Errors.Count > 0)
            return BadRequest(new { message = $"ไฟล์มีข้อผิดพลาด {plan.Errors.Count} จุด — แก้ไฟล์ก่อนนำเข้า", errors = plan.Errors.Take(200) });

        var user = receivedBy.Trim();
        var receiptNo = $"OB-{DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)}";
        using var tx = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory" ? null : _context.Database.BeginTransaction();
        try
        {
            // Snapshot record so it shows in receipt history — one line per part/condition with the
            // quantity the warehouse was SET to (not an amount added).
            var receipt = new GoodsReceipt
            {
                ReceiptNo = receiptNo, Source = "OpeningBalance", LocationId = locationId,
                ReceivedBy = user, ReceivedAt = DateTime.Now,
                RefDocument = $"ยอดตั้งต้นจากไฟล์ {file.FileName}"
            };
            foreach (var p in plan.Parts)
            {
                if (p.NewGood > 0) receipt.Lines.Add(new GoodsReceiptLine { PartId = p.PartId, PartNo = p.PartNo, Qty = p.NewGood, Condition = "Good", Remarks = $"ยอดตั้งต้น (เดิม {p.CurGood})" });
                if (p.NewRepair > 0) receipt.Lines.Add(new GoodsReceiptLine { PartId = p.PartId, PartNo = p.PartNo, Qty = p.NewRepair, Condition = "Bad", Remarks = $"ยอดตั้งต้น รอซ่อม (เดิม {p.CurRepair})" });
            }
            _context.GoodsReceipts.Add(receipt);
            _context.SaveChanges();
            var refId = receipt.Id.ToString();

            // 1) Set warehouse stock to the file's numbers.
            foreach (var p in plan.Parts)
            {
                if (p.NewGood != p.CurGood)
                    _stock.AdjustStock(p.PartNo, locationId, p.NewGood - p.CurGood, "Good", "OpeningBalance", "OpeningBalance", refId, user, $"ตั้งยอด {p.CurGood} → {p.NewGood}");
                if (p.NewRepair != p.CurRepair)
                    _stock.AdjustStock(p.PartNo, locationId, p.NewRepair - p.CurRepair, "Repair", "OpeningBalance", "OpeningBalance", refId, user, $"ตั้งยอดรอซ่อม {p.CurRepair} → {p.NewRepair}");
            }

            // 2) Serials: register new ones; move existing ones here. A unit pulled back from another
            //    location takes one piece off that location's count (never below zero).
            var fromDelta = new Dictionary<(string partNo, int loc, string cond), int>();
            foreach (var sc in plan.Serials)
            {
                PartUnit unit;
                if (sc.UnitId == null)
                {
                    unit = new PartUnit { SerialNo = sc.Serial, PartId = sc.PartId, ReceivedAt = sc.ReceivedAt ?? DateTime.Now };
                    _context.PartUnits.Add(unit);
                }
                else
                {
                    unit = _context.PartUnits.First(u => u.Id == sc.UnitId);
                    if (sc.Kind != "here" && sc.FromLocationId.HasValue)
                    {
                        var cond = sc.FromStatus == "InRepair" ? "Repair" : "Good";
                        var key = (sc.PartNo, sc.FromLocationId.Value, cond);
                        fromDelta[key] = fromDelta.GetValueOrDefault(key) + 1;
                    }
                    unit.PartId = sc.PartId;
                }
                unit.LocationId = locationId;
                unit.Status = sc.Bad ? "InRepair" : "InStock";
                unit.Condition = sc.Bad ? "Bad" : "Good";
            }
            foreach (var ((partNo, loc, cond), n) in fromDelta)
            {
                var part = _context.Parts.First(p => p.PartNo == partNo);
                var st = _context.PartStocks.Local.FirstOrDefault(x => x.PartId == part.Id && x.LocationId == loc)
                         ?? _context.PartStocks.FirstOrDefault(x => x.PartId == part.Id && x.LocationId == loc);
                var have = st == null ? 0 : (cond == "Repair" ? st.RepairQty : st.GoodQty);
                var take = Math.Min(have, n);
                if (take > 0)
                    _stock.AdjustStock(partNo, loc, -take, cond, "OpeningBalance", "OpeningBalance", refId, user, $"S/N {take} ชิ้นพบอยู่ในคลังตามยอดตั้งต้น");
            }

            // 3) Projects.
            foreach (var pc in plan.Projects)
                _context.Parts.First(p => p.Id == pc.PartId).Project = pc.To;

            _context.SaveChanges();
            tx?.Commit();

            _audit.Log(User, "Stock", location.Code, "OPENING_BALANCE",
                System.Text.Json.JsonSerializer.Serialize(new { goodBefore = plan.Parts.Sum(x => x.CurGood), repairBefore = plan.Parts.Sum(x => x.CurRepair) }),
                new { receiptNo, file = file.FileName, sheet = plan.SheetName, goodAfter = plan.Parts.Sum(x => x.NewGood), repairAfter = plan.Parts.Sum(x => x.NewRepair),
                      partsChanged = plan.Parts.Count, serials = plan.Serials.Count, projects = plan.Projects.Count });

            return Ok(new
            {
                message = "ตั้งยอดตั้งต้นเรียบร้อย",
                receiptNo,
                goodQty = plan.Parts.Sum(x => x.NewGood),
                repairQty = plan.Parts.Sum(x => x.NewRepair),
                partsChanged = plan.Parts.Count(x => x.CurGood != x.NewGood || x.CurRepair != x.NewRepair),
                serials = plan.Serials.Count,
                projects = plan.Projects.Count
            });
        }
        catch (Exception ex)
        {
            tx?.Rollback();
            return BadRequest(new { message = $"บันทึกไม่สำเร็จ ไม่มีข้อมูลใดถูกเปลี่ยน: {ex.Message}" });
        }
    }
}
