using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DemoController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly StockService _stock;

    public DemoController(AppDbContext context, StockService stock)
    {
        _context = context;
        _stock = stock;
    }

    // POST /api/Demo/seed
    [HttpPost("seed")]
    public IActionResult Seed()
    {
        if (_context.GoodsReceipts.Any())
            return BadRequest(new { message = "Demo data already seeded. DELETE /api/Demo/clear first." });

        var parts = _context.Parts.OrderBy(p => p.PartNo).Take(60).ToList();
        if (!parts.Any()) return BadRequest(new { message = "No parts found. Seed parts first." });

        // ── Ensure Locations ───────────────────────────────────────────────
        if (!_context.Locations.Any())
        {
            _context.Locations.AddRange(
                new Location { Name = "DHL Center Bangkok",    Code = "DHL-BKK",   LocationType = "DHL_CENTER",     IsActive = true },
                new Location { Name = "คลัง Ratchaburana",     Code = "WH-RATCH",  LocationType = "RATCHABURANA",   IsActive = true },
                new Location { Name = "GRG Repair Center",     Code = "GRG-RC",    LocationType = "GRG",            IsActive = true },
                new Location { Name = "Technician On-Location",Code = "OL-TECH",   LocationType = "OL_TECHNICIAN",  IsActive = true },
                new Location { Name = "In Transit",            Code = "TRANSIT",   LocationType = "IN_TRANSIT",     IsActive = true },
                new Location { Name = "SCRAP / Disposal",      Code = "SCRAP",     LocationType = "SCRAP",          IsActive = true }
            );
            _context.SaveChanges();
        }

        // ── Ensure Vendors ─────────────────────────────────────────────────
        if (!_context.Vendors.Any())
        {
            _context.Vendors.AddRange(
                new Vendor { Name = "GRG Banking Equipment Co., Ltd.", Code = "GRG",       VendorType = "GRG",   ContactInfo = "grg-support@grg.com",     IsActive = true },
                new Vendor { Name = "ABC Electronics Thailand",        Code = "LOCAL-001", VendorType = "LOCAL", ContactInfo = "sales@abc-elect.co.th",   IsActive = true },
                new Vendor { Name = "KP Tech Supply",                  Code = "LOCAL-002", VendorType = "LOCAL", ContactInfo = "procurement@kptech.co.th", IsActive = true }
            );
            _context.SaveChanges();
        }

        var locDhl   = _context.Locations.First(l => l.LocationType == "DHL_CENTER");
        var locWh    = _context.Locations.First(l => l.LocationType == "RATCHABURANA");
        var locOl    = _context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var locScrap = _context.Locations.First(l => l.LocationType == "SCRAP");
        var vendorGrg   = _context.Vendors.First(v => v.VendorType == "GRG");
        var vendorLocal = _context.Vendors.First(v => v.VendorType == "LOCAL");

        // ── สต็อกตัวอย่าง: qty สุ่มตาม pattern ───────────────────────────
        // group 1: high stock (15-30)  → parts 0-19
        // group 2: medium stock (5-14) → parts 20-39
        // group 3: low/zero stock      → parts 40-59
        int Qty(int idx) => idx < 20 ? 15 + (idx % 10) : idx < 40 ? 5 + (idx % 8) : idx % 3;

        // ── GR-001: GRG shipment (40 parts) ────────────────────────────────
        var gr1 = new GoodsReceipt
        {
            ReceiptNo   = "GR-2025-001",
            Source      = "GRG",
            VendorId    = vendorGrg.Id,
            LocationId  = locDhl.Id,
            RefDocument = "FORECAST-LOT-2025-Q1",
            ReceivedBy  = "วิชัย สมใจ",
            ReceivedAt  = DateTime.Now.AddDays(-45),
            HandlingCost = 1200m,
        };
        _context.GoodsReceipts.Add(gr1);
        _context.SaveChanges();

        for (int i = 0; i < 40; i++)
        {
            var p = parts[i];
            var qty = Qty(i);
            if (qty == 0) continue;
            _context.GoodsReceiptLines.Add(new GoodsReceiptLine
            {
                GoodsReceiptId = gr1.Id,
                PartNo         = p.PartNo,
                Qty            = qty,
                Condition      = "Good",
                Remarks        = i < 20 ? "สภาพดี" : "ตรวจสอบแล้ว",
            });
            _stock.AdjustStock(p.PartNo, locDhl.Id, qty, "Good",
                "GR", "GoodsReceipt", gr1.Id.ToString(), "system", $"Opening stock via {gr1.ReceiptNo}");
        }

        // ── GR-002: Local vendor (20 parts, some defective) ─────────────────
        var gr2 = new GoodsReceipt
        {
            ReceiptNo   = "GR-2025-002",
            Source      = "LocalVendor",
            VendorId    = vendorLocal.Id,
            LocationId  = locWh.Id,
            RefDocument = "PO-2025-0042",
            ReceivedBy  = "สมหญิง รักดี",
            ReceivedAt  = DateTime.Now.AddDays(-20),
            HandlingCost = 350m,
        };
        _context.GoodsReceipts.Add(gr2);
        _context.SaveChanges();

        for (int i = 40; i < Math.Min(60, parts.Count); i++)
        {
            var p   = parts[i];
            var qty = 3 + (i % 5);
            _context.GoodsReceiptLines.Add(new GoodsReceiptLine
            {
                GoodsReceiptId = gr2.Id,
                PartNo         = p.PartNo,
                Qty            = qty,
                Condition      = i % 7 == 0 ? "Defective" : "Good",
                Remarks        = i % 7 == 0 ? "กล่องบุบ ของชำรุด" : null,
            });
            var cond = i % 7 == 0 ? "Defective" : "Good";
            _stock.AdjustStock(p.PartNo, locWh.Id, qty, cond,
                "GR", "GoodsReceipt", gr2.Id.ToString(), "system", $"Local purchase via {gr2.ReceiptNo}");
        }

        _context.SaveChanges();

        // ── Tickets (เบิก/ยืม/คืน — synced from Aservice) ────────────────────
        var techs = new[]
        {
            ("สมชาย จริงใจ","somchai@tech.com","Field Team A"),
            ("วิภา สุขใส",  "wipa@tech.com",   "Field Team B"),
            ("ประยุทธ์ ดีดี","prayuth@tech.com","Field Team C"),
            ("มาลี รุ่งเรือง","malee@tech.com", "Maintenance"),
            ("สุพัตรา ใจดี", "supatra@tech.com","Field Team A"),
        };

        TicketPartLine Line(int ticketId, Part p, int qty, string lineType) => new()
        { TicketId = ticketId, PartId = p.Id, PartNo = p.PartNo, Quantity = qty, LineType = lineType };

        // Ticket 1 — เบิก (ช่างได้รับของแล้ว)
        var p1 = parts[2];
        var tk1 = new Ticket
        {
            ExternalTicketNo = "ASV-DEMO-001", TechName = techs[0].Item1, TechEmail = techs[0].Item2, TechDept = techs[0].Item3,
            Status = "เบิก", WithdrawAddress = "สาขาสยาม กทม.",
            ApproverName = "admin@atm.com", ApprovedAt = DateTime.Now.AddDays(-29),
            CreatedAt = DateTime.Now.AddDays(-30), UpdatedAt = DateTime.Now.AddDays(-28),
        };
        _context.Tickets.Add(tk1);
        _context.SaveChanges();
        _context.TicketPartLines.Add(Line(tk1.TicketId, p1, 1, "Withdraw"));
        _context.SaveChanges();
        _stock.AdjustStock(p1.PartNo, locDhl.Id, -1, "Good",
            "Issue", "Ticket", tk1.TicketId.ToString(), techs[0].Item1, "เบิกให้ช่าง " + techs[0].Item1);
        _context.SaveChanges();

        // Ticket 2 — เดินทาง (admin อนุมัติแล้ว รอช่างรับ)
        var p2 = parts[5];
        var tk2 = new Ticket
        {
            ExternalTicketNo = "ASV-DEMO-002", TechName = techs[1].Item1, TechEmail = techs[1].Item2, TechDept = techs[1].Item3,
            Status = "เดินทาง", WithdrawAddress = "สาขาลาดพร้าว กทม.",
            ApproverName = "admin@atm.com", ApprovedAt = DateTime.Now.AddDays(-4),
            CreatedAt = DateTime.Now.AddDays(-5), UpdatedAt = DateTime.Now.AddDays(-4),
        };
        _context.Tickets.Add(tk2);
        _context.SaveChanges();
        _context.TicketPartLines.Add(Line(tk2.TicketId, p2, 1, "Withdraw"));
        _context.SaveChanges();
        _stock.AdjustStock(p2.PartNo, locDhl.Id, -1, "Good",
            "Issue", "Ticket", tk2.TicketId.ToString(), techs[1].Item1, "อนุมัติเบิก");
        _context.SaveChanges();

        // Ticket 3 — รอ (รอ admin อนุมัติ)
        var p3 = parts[8];
        var tk3 = new Ticket
        {
            ExternalTicketNo = "ASV-DEMO-003", TechName = techs[2].Item1, TechEmail = techs[2].Item2, TechDept = techs[2].Item3,
            Status = "รอ", WithdrawAddress = "สาขาบางนา กทม.",
            CreatedAt = DateTime.Now.AddDays(-2), UpdatedAt = DateTime.Now.AddDays(-2),
        };
        _context.Tickets.Add(tk3);
        _context.SaveChanges();
        _context.TicketPartLines.Add(Line(tk3.TicketId, p3, 1, "Withdraw"));
        _context.SaveChanges();

        // Ticket 4 — รอ (ขาคืน — ช่างส่งคำขอคืนแล้ว รอจัดส่ง)
        var p4 = parts[12];
        var tk4 = new Ticket
        {
            ExternalTicketNo = "ASV-DEMO-004", TechName = techs[3].Item1, TechEmail = techs[3].Item2, TechDept = techs[3].Item3,
            Status = "รอ", WithdrawAddress = "สาขารามคำแหง กทม.", ReturnAddress = "DHL Hub รามคำแหง",
            ApproverName = "admin@atm.com", ApprovedAt = DateTime.Now.AddDays(-9),
            CreatedAt = DateTime.Now.AddDays(-10), UpdatedAt = DateTime.Now.AddDays(-1),
        };
        _context.Tickets.Add(tk4);
        _context.SaveChanges();
        _context.TicketPartLines.Add(Line(tk4.TicketId, p4, 1, "Withdraw"));
        _context.TicketPartLines.Add(Line(tk4.TicketId, p4, 1, "Return"));
        _context.SaveChanges();
        _stock.AdjustStock(p4.PartNo, locDhl.Id, -1, "Good",
            "Issue", "Ticket", tk4.TicketId.ToString(), techs[3].Item1, "เบิกให้ช่าง " + techs[3].Item1);
        _context.SaveChanges();

        // Ticket 5 — คืน (จบขาคืนแล้ว)
        var p5 = parts[15];
        var tk5 = new Ticket
        {
            ExternalTicketNo = "ASV-DEMO-005", TechName = techs[4].Item1, TechEmail = techs[4].Item2, TechDept = techs[4].Item3,
            Status = "คืน", WithdrawAddress = "สาขาสีลม กทม.", ReturnAddress = "DHL Hub สีลม",
            ApproverName = "admin@atm.com", ApprovedAt = DateTime.Now.AddDays(-19),
            CreatedAt = DateTime.Now.AddDays(-20), UpdatedAt = DateTime.Now.AddDays(-3),
        };
        _context.Tickets.Add(tk5);
        _context.SaveChanges();
        _context.TicketPartLines.Add(Line(tk5.TicketId, p5, 1, "Withdraw"));
        _context.TicketPartLines.Add(Line(tk5.TicketId, p5, 1, "Return"));
        _context.SaveChanges();
        _stock.AdjustStock(p5.PartNo, locDhl.Id, -1, "Good",
            "Issue", "Ticket", tk5.TicketId.ToString(), techs[4].Item1, "เบิกให้ช่าง " + techs[4].Item1);
        _context.SaveChanges();

        // ── Return ─────────────────────────────────────────────────────────
        // คืนจาก Ticket 1 (สภาพ Defective)
        var ret1 = new ReturnRequest
        {
            TicketId       = tk1.TicketId,
            PartNo         = p1.PartNo,
            Condition      = "Defective",
            SourceType     = "Technician",
            LocationFromId = locOl.Id,
            LocationToId   = locScrap.Id,
            ReturnedBy     = techs[0].Item1,
            CreatedAt      = DateTime.Now.AddDays(-25),
        };
        _context.ReturnRequests.Add(ret1);
        _context.SaveChanges();
        _stock.AdjustStock(p1.PartNo, locScrap.Id, 1, "Defective",
            "Return", "Ticket", tk1.TicketId.ToString(), techs[0].Item1, "คืนอะไหล่ชำรุด");
        _context.SaveChanges();

        var summary = new
        {
            message = "✅ Demo data seeded successfully",
            goods_receipts = 2,
            gr1_parts = 40,
            gr2_parts = 20,
            tickets = 5,
            returns = 1,
            locations_seeded = !_context.Locations.Any() ? 0 : _context.Locations.Count(),
            vendors_seeded   = !_context.Vendors.Any()   ? 0 : _context.Vendors.Count(),
            total_stock_movements = _context.StockMovements.Count(),
        };
        return Ok(summary);
    }

    // DELETE /api/Demo/clear  — ล้างข้อมูล demo ทิ้ง (ไม่ลบ Parts/Categories)
    [HttpDelete("clear")]
    public IActionResult Clear()
    {
        _context.ReturnRequests.RemoveRange(_context.ReturnRequests);
        _context.TicketPartLines.RemoveRange(_context.TicketPartLines);
        _context.Tickets.RemoveRange(_context.Tickets);
        _context.GoodsReceiptLines.RemoveRange(_context.GoodsReceiptLines);
        _context.GoodsReceipts.RemoveRange(_context.GoodsReceipts);
        _context.StockMovements.RemoveRange(_context.StockMovements);
        _context.Set<PartStock>().RemoveRange(_context.Set<PartStock>());
        _context.Set<PartUnit>().RemoveRange(_context.Set<PartUnit>());

        // Clearing PartStock zeroes out on-hand stock — there is no per-part total to reset.
        _context.SaveChanges();
        return Ok(new { message = "Demo data cleared. Parts and Categories are preserved." });
    }

    // GET /api/Demo/status
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        parts         = _context.Parts.Count(),
        categories    = _context.Categories.Count(),
        locations     = _context.Locations.Count(),
        vendors       = _context.Vendors.Count(),
        goods_receipts = _context.GoodsReceipts.Count(),
        tickets       = _context.Tickets.Count(),
        returns       = _context.ReturnRequests.Count(),
        stock_movements = _context.StockMovements.Count(),
        parts_with_stock = _context.PartStocks
            .GroupBy(s => s.PartId)
            .Count(g => g.Sum(x => x.GoodQty) > 0),
    });
}
