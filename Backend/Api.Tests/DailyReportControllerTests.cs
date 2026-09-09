using Api.Controllers;
using Api.Models;
using Api.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Covers DailyReportController's matching rules against the DHL "Return inbound" sheet shape:
/// first-time return confirmation (GOOD → stock, BAD → RepairQty + PartUnit InRepair), a repaired
/// Serial No. reappearing as GOOD in a later import closing the repair loop, and that re-importing
/// an already-processed file is a safe no-op (see DailyReportController.Process for the rules).
/// </summary>
public class DailyReportControllerTests
{
    private const string PartNo = "DR-TEST-PART";

    private static (TicketController Tickets, DailyReportController DailyReport, AppDbContext Context, Location MainWh, Location TechLoc) Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);

        var part = new Part { PartNo = PartNo, PartName = "Test Part", IsActive = true };
        var mainWh = new Location { Code = "DHL-BKK", Name = "DHL Center Bangkok", LocationType = "DHL_CENTER", IsActive = true };
        var techLoc = new Location { Code = "OL-TECH", Name = "Technician Stock", LocationType = "OL_TECHNICIAN", IsActive = true };
        var ratWh = new Location { Code = "WH-RAT", Name = "Ratchaburana Warehouse", LocationType = "RATCHABURANA", IsActive = true };
        context.Parts.Add(part);
        context.Locations.AddRange(mainWh, techLoc, ratWh);
        context.SaveChanges();
        context.PartStocks.Add(new PartStock { PartId = part.Id, LocationId = mainWh.Id, GoodQty = 100, BadQty = 0 });
        context.PartStocks.Add(new PartStock { PartId = part.Id, LocationId = ratWh.Id, GoodQty = 0, BadQty = 0, RepairQty = 0 });
        context.SaveChanges();

        var stock = new StockService(context);
        var audit = new AuditService(context);
        var config = new ConfigurationBuilder().Build();
        var env = new FakeEnv();

        var tickets = new TicketController(context, stock, audit, config, env);
        var dailyReport = new DailyReportController(context, stock, audit);
        return (tickets, dailyReport, context, mainWh, techLoc);
    }

    /// <summary>Drives a Ticket's one WithdrawBatch all the way to เดินทาง on its own return leg
    /// ("คืนตามใบเบิก" — ready for DHL to confirm).</summary>
    private static (Ticket Ticket, WithdrawBatch Batch) CreateShippedReturnTicket(TicketController tickets, AppDbContext context, string externalNo, int qty = 1)
    {
        tickets.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = externalNo, TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == externalNo);
        tickets.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto { Lines = new() { new LineDto { PartNo = PartNo, Quantity = qty } }, Address = "Addr" });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        // Sufficient stock (fixture seeds 100) means Admin's approve alone gets this to
        // "รอส่งเมล DHL" — the real "เดินทาง" transition only happens once Admin then confirms the
        // DHL email went out.
        tickets.ApproveBatch(ticket.TicketId, batch.WithdrawBatchId);
        tickets.SendEmailConfirmedBatch(ticket.TicketId, batch.WithdrawBatchId);
        tickets.ReceiveBatch(ticket.TicketId, batch.WithdrawBatchId);
        tickets.SubmitReturn(ticket.TicketId, batch.WithdrawBatchId, new SubmitLinesDto { Lines = new() { new LineDto { PartNo = PartNo, Quantity = qty, Condition = "Good" } }, Address = "Return Addr" });
        tickets.ApproveReturn(ticket.TicketId, batch.WithdrawBatchId);
        tickets.SendEmailConfirmedReturn(ticket.TicketId, batch.WithdrawBatchId);
        tickets.MarkShipped(ticket.TicketId, batch.WithdrawBatchId);
        return (context.Tickets.First(t => t.TicketId == ticket.TicketId), context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId));
    }

    private static IFormFile BuildDailyReportFile(params (string PartNo, string PartName, string Serial, int Qty, string Status, string? Problem)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Return inbound");
        ws.Cell(2, 1).Value = "No";
        ws.Cell(2, 2).Value = "Part Number";
        ws.Cell(2, 3).Value = "Part Description";
        ws.Cell(2, 4).Value = "SERIAL_NUMBER";
        ws.Cell(2, 5).Value = "QTY";
        ws.Cell(2, 6).Value = "INVENTORY STATUS";
        ws.Cell(2, 7).Value = "Problem";

        int r = 3;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = r - 2;
            ws.Cell(r, 2).Value = row.PartNo;
            ws.Cell(r, 3).Value = row.PartName;
            ws.Cell(r, 4).Value = row.Serial;
            ws.Cell(r, 5).Value = row.Qty;
            ws.Cell(r, 6).Value = row.Status;
            ws.Cell(r, 7).Value = row.Problem ?? "";
            r++;
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "daily-report.xlsx") { Headers = new HeaderDictionary(), ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
    }

    // Mar 2026+ exports add a "Case No" column — this builds that newer shape.
    private static IFormFile BuildDailyReportFileWithCaseNo(params (string PartNo, string PartName, string Serial, int Qty, string Status, string? Problem, string CaseNo)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Return inbound");
        ws.Cell(2, 1).Value = "No";
        ws.Cell(2, 2).Value = "Part Number";
        ws.Cell(2, 3).Value = "Part Description";
        ws.Cell(2, 4).Value = "SERIAL_NUMBER";
        ws.Cell(2, 5).Value = "QTY";
        ws.Cell(2, 6).Value = "INVENTORY STATUS";
        ws.Cell(2, 7).Value = "Problem";
        ws.Cell(2, 8).Value = "Case No";

        int r = 3;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = r - 2;
            ws.Cell(r, 2).Value = row.PartNo;
            ws.Cell(r, 3).Value = row.PartName;
            ws.Cell(r, 4).Value = row.Serial;
            ws.Cell(r, 5).Value = row.Qty;
            ws.Cell(r, 6).Value = row.Status;
            ws.Cell(r, 7).Value = row.Problem ?? "";
            ws.Cell(r, 8).Value = row.CaseNo;
            r++;
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "daily-report.xlsx") { Headers = new HeaderDictionary(), ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
    }

    [Fact]
    public void Preview_MatchesOpenReturnTicket_ButDoesNotPersist()
    {
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var (ticket, batch) = CreateShippedReturnTicket(tickets, context, "DR-1");
        var file = BuildDailyReportFile((PartNo, "Test Part", "SN-001", 1, "GOOD", null));

        var result = dailyReport.Preview(file);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("เดินทาง", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).ReturnStatus); // untouched
        Assert.Equal(99, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // 100 - 1 (withdrawn), untouched by preview
    }

    [Fact]
    public void Confirm_GoodRow_ConfirmsReturnAndAddsToGoodStock()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        var (ticket, batch) = CreateShippedReturnTicket(tickets, context, "DR-2");
        var file = BuildDailyReportFile((PartNo, "Test Part", "SN-002", 1, "GOOD", null));

        var result = dailyReport.Confirm(file);

        Assert.IsType<OkObjectResult>(result);
        var batchAfter = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId);
        Assert.Equal("คืน", batchAfter.ReturnStatus);
        Assert.Equal(100, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // 100 - 1 (withdrawn) + 1 (returned)
        Assert.True(context.PartUnits.Any(u => u.SerialNo == "SN-002" && u.Status == "InStock"));

        // Serial No. must land on the StockMovement ledger itself (not just in Remarks text) —
        // that's what the existing Serial Tracking page (TrackingController.bySerial) queries by.
        Assert.True(context.StockMovements.Any(m => m.SerialNo == "SN-002"));
    }

    [Fact]
    public void Confirm_BadRow_SendsToRepairQty_NotBadQty()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        CreateShippedReturnTicket(tickets, context, "DR-3");
        var file = BuildDailyReportFile((PartNo, "Test Part", "SN-003", 1, "BAD", "สายพานขาด"));

        dailyReport.Confirm(file);

        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        Assert.Equal(99, stock.GoodQty); // 100 - 1 (withdrawn), unchanged by this import — went to RepairQty instead
        Assert.Equal(1, stock.RepairQty);
        Assert.Equal(0, stock.BadQty);
        var unit = context.PartUnits.First(u => u.SerialNo == "SN-003");
        Assert.Equal("InRepair", unit.Status);
    }

    [Fact]
    public void Confirm_SameSerialLaterGood_ClosesTheRepairLoop()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        CreateShippedReturnTicket(tickets, context, "DR-4");

        // First Daily Report: comes back Bad, goes to repair.
        dailyReport.Confirm(BuildDailyReportFile((PartNo, "Test Part", "SN-004", 1, "BAD", "จอดำ")));
        Assert.Equal(1, context.PartStocks.First(s => s.LocationId == mainWh.Id).RepairQty);

        // Weeks later, a second Daily Report shows the same Serial back as Good.
        var result = dailyReport.Confirm(BuildDailyReportFile((PartNo, "Test Part", "SN-004", 1, "GOOD", null)));

        Assert.IsType<OkObjectResult>(result);
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        Assert.Equal(0, stock.RepairQty);
        Assert.Equal(100, stock.GoodQty); // 100 - 1 (withdrawn) + 1 (repair complete)
        Assert.Equal("InStock", context.PartUnits.First(u => u.SerialNo == "SN-004").Status);
    }

    [Fact]
    public void Confirm_ReimportingSameFile_IsUnmatchedNotDoubleCounted()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        CreateShippedReturnTicket(tickets, context, "DR-5");

        dailyReport.Confirm(BuildDailyReportFile((PartNo, "Test Part", "SN-005", 1, "GOOD", null)));
        var afterFirst = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;

        // Admin accidentally re-imports the exact same file a second time.
        var second = dailyReport.Confirm(BuildDailyReportFile((PartNo, "Test Part", "SN-005", 1, "GOOD", null)));

        var ok = Assert.IsType<OkObjectResult>(second);
        var afterSecond = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        Assert.Equal(afterFirst, afterSecond); // no double-counting
    }

    [Fact]
    public void Confirm_NoOpenReturnAtAll_StillCreditsCentralWarehouse()
    {
        // No Ticket, no batch, nothing waiting — DHL genuinely received a part our app never
        // tracked a return for (e.g. the tech's return request never reached "เดินทาง" in-app).
        // The physical stock still has to show up so techs can withdraw against it same-day.
        var (_, dailyReport, context, mainWh, _) = Create();
        var file = BuildDailyReportFile((PartNo, "Test Part", "SN-NOTICKET-001", 2, "GOOD", null));

        var result = Assert.IsType<OkObjectResult>(dailyReport.Confirm(file));

        Assert.Equal(102, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // 100 + 2
        Assert.Equal("InStock", context.PartUnits.First(u => u.SerialNo == "SN-NOTICKET-001").Status);
    }

    [Fact]
    public void UndoRow_UnmatchedButCredited_RevertsStock()
    {
        var (_, dailyReport, context, mainWh, _) = Create();
        var file = BuildDailyReportFile((PartNo, "Test Part", "SN-NOTICKET-002", 1, "GOOD", null));
        var confirmResult = Assert.IsType<OkObjectResult>(dailyReport.Confirm(file));

        var jsonOptions = new System.Text.Json.JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
        var json = System.Text.Json.JsonSerializer.Serialize(confirmResult.Value, jsonOptions);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var batchId = doc.RootElement.GetProperty("batch").GetProperty("Id").GetInt32();
        var row = context.DailyReportImportRows.First(r => r.BatchId == batchId);

        Assert.True(row.StockCredited);
        Assert.Equal(101, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty);

        var undoResult = dailyReport.UndoRow(row.Id);

        Assert.IsType<OkObjectResult>(undoResult);
        Assert.Equal(100, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // back to baseline
        Assert.Null(context.PartUnits.FirstOrDefault(u => u.SerialNo == "SN-NOTICKET-002"));
        Assert.True(context.DailyReportImportRows.First(r => r.Id == row.Id).Undone);
    }

    [Fact]
    public void UndoRow_ReturnConfirmed_RevertsStockAndReopensTicket()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        var (ticket, batch) = CreateShippedReturnTicket(tickets, context, "DR-6");

        var confirmResult = Assert.IsType<OkObjectResult>(dailyReport.Confirm(BuildDailyReportFile((PartNo, "Test Part", "SN-006", 1, "GOOD", null))));
        // Anonymous response type is internal to the Api assembly — read it back via JSON rather
        // than dynamic/reflection (see TicketWorkflowTests for the same pattern).
        var jsonOptions = new System.Text.Json.JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
        var json = System.Text.Json.JsonSerializer.Serialize(confirmResult.Value, jsonOptions);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var batchId = doc.RootElement.GetProperty("batch").GetProperty("Id").GetInt32();
        var row = context.DailyReportImportRows.First(r => r.BatchId == batchId);

        var undoResult = dailyReport.UndoRow(row.Id);

        Assert.IsType<OkObjectResult>(undoResult);
        Assert.Equal(99, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // back to withdrawn-only baseline
        Assert.Equal("เดินทาง", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).ReturnStatus);
        Assert.True(context.DailyReportImportRows.First(r => r.Id == row.Id).Undone);
    }

    // ── Case No. matching (Mar 2026+ exports) ──────────────────────────────────

    [Fact]
    public void Confirm_WithCaseNo_MatchesExactTicket_EvenWhenFifoWouldGuessWrong()
    {
        // Two techs both return the same Part No. — Ticket "OLDER" shipped its return first
        // (so plain FIFO would pick it), but the Daily Report row's Case No. actually belongs to
        // Ticket "NEWER". Case No. must win over the FIFO guess.
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var (older, olderBatch) = CreateShippedReturnTicket(tickets, context, "OLDER");
        var (newer, newerBatch) = CreateShippedReturnTicket(tickets, context, "NEWER");

        var file = BuildDailyReportFileWithCaseNo((PartNo, "Test Part", "SN-CASE-001", 1, "GOOD", null, "NEWER"));
        var result = dailyReport.Confirm(file);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("คืน", context.WithdrawBatches.First(b => b.WithdrawBatchId == newerBatch.WithdrawBatchId).ReturnStatus); // the actual match
        Assert.Equal("เดินทาง", context.WithdrawBatches.First(b => b.WithdrawBatchId == olderBatch.WithdrawBatchId).ReturnStatus); // untouched, despite being "older"
    }

    [Fact]
    public void Confirm_WithCaseNoButNoMatchingTicket_IsUnmatched_NotAGuess()
    {
        // A row carries a Case No. that doesn't correspond to any open return line — must not
        // silently fall back to guessing by Part No. against some unrelated open ticket. The
        // unrelated Ticket's own batch must stay untouched either way; the part itself still
        // credits the central warehouse (a brand-new serial DHL genuinely received), just with
        // no Ticket/WithdrawBatch tied to it — see Process, Rule 3.
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var (ticket, batch) = CreateShippedReturnTicket(tickets, context, "DR-CASE-2");
        var file = BuildDailyReportFileWithCaseNo((PartNo, "Test Part", "SN-CASE-002", 1, "GOOD", null, "NO-SUCH-CASE"));

        var result = dailyReport.Confirm(file);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("เดินทาง", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).ReturnStatus); // untouched
        Assert.Equal(100, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // credited anyway (99 + 1 new serial)
    }

    [Fact]
    public void Confirm_WithoutCaseNoColumn_StillFallsBackToPartNoGuess()
    {
        // Older-format files (no Case No. column at all) must keep working exactly as before.
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var (ticket, batch) = CreateShippedReturnTicket(tickets, context, "DR-NOCASE");
        var file = BuildDailyReportFile((PartNo, "Test Part", "SN-NOCASE-001", 1, "GOOD", null));

        var result = dailyReport.Confirm(file);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("คืน", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).ReturnStatus);
    }

    private static IFormFile BuildOutboundDailyReportFile(params (string PartNo, string PartName, string Serial, int Qty, string FeName, string CaseNo)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Outbound Order ");
        ws.Cell(1, 1).Value = "No";
        ws.Cell(1, 2).Value = "Part Number";
        ws.Cell(1, 3).Value = "Part Description";
        ws.Cell(1, 4).Value = "SERIAL_NUMBER";
        ws.Cell(1, 5).Value = "QTY";
        ws.Cell(1, 6).Value = "FE Name";
        ws.Cell(1, 20).Value = "Case No";
        ws.Cell(1, 26).Value = "Inventory Status";

        int r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = r - 1;
            ws.Cell(r, 2).Value = row.PartNo;
            ws.Cell(r, 3).Value = row.PartName;
            ws.Cell(r, 4).Value = row.Serial;
            ws.Cell(r, 5).Value = row.Qty;
            ws.Cell(r, 6).Value = row.FeName;
            ws.Cell(r, 20).Value = row.CaseNo;
            ws.Cell(r, 26).Value = "GOOD";
            r++;
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "daily-report-outbound.xlsx") { Headers = new HeaderDictionary(), ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
    }

    private static IFormFile BuildOutboundDailyReportFileWithDate(params (string PartNo, string PartName, string Serial, int Qty, string FeName, string CaseNo, DateTime? Date)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Outbound Order ");
        ws.Cell(1, 1).Value = "No";
        ws.Cell(1, 2).Value = "Part Number";
        ws.Cell(1, 3).Value = "Part Description";
        ws.Cell(1, 4).Value = "SERIAL_NUMBER";
        ws.Cell(1, 5).Value = "QTY";
        ws.Cell(1, 6).Value = "FE Name";
        ws.Cell(1, 20).Value = "Case No";
        ws.Cell(1, 25).Value = "CREATION_DATE";
        ws.Cell(1, 26).Value = "Inventory Status";

        int r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = r - 1;
            ws.Cell(r, 2).Value = row.PartNo;
            ws.Cell(r, 3).Value = row.PartName;
            ws.Cell(r, 4).Value = row.Serial;
            ws.Cell(r, 5).Value = row.Qty;
            ws.Cell(r, 6).Value = row.FeName;
            ws.Cell(r, 20).Value = row.CaseNo;
            if (row.Date.HasValue) ws.Cell(r, 25).Value = row.Date.Value;
            ws.Cell(r, 26).Value = "GOOD";
            r++;
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "daily-report-outbound-date.xlsx") { Headers = new HeaderDictionary(), ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
    }

    private static IFormFile BuildMultiSheetReportFile(
        (string PartNo, string Serial, int Qty, string CaseNo, string FeName)[] outbound,
        (string PartNo, string Serial, int Qty)[] inboundNormal,
        (string PartNo, int AvailQty, int MinQty)[] minStock,
        (string PartNo, string Serial, int Qty, string CaseNo, string CustSite)[]? outboundExport = null)
    {
        using var wb = new XLWorkbook();

        // Outbound sheet
        var wsOut = wb.Worksheets.Add("Outbound Order ");
        wsOut.Cell(1, 1).Value = "No";
        wsOut.Cell(1, 2).Value = "Part Number";
        wsOut.Cell(1, 3).Value = "Part Description";
        wsOut.Cell(1, 4).Value = "SERIAL_NUMBER";
        wsOut.Cell(1, 5).Value = "QTY";
        wsOut.Cell(1, 6).Value = "FE Name";
        wsOut.Cell(1, 20).Value = "Case No";
        int r = 2;
        foreach (var row in outbound)
        {
            wsOut.Cell(r, 1).Value = r - 1;
            wsOut.Cell(r, 2).Value = row.PartNo;
            wsOut.Cell(r, 4).Value = row.Serial;
            wsOut.Cell(r, 5).Value = row.Qty;
            wsOut.Cell(r, 6).Value = row.FeName;
            wsOut.Cell(r, 20).Value = row.CaseNo;
            r++;
        }

        // Outbound Orde Export sheet (sending defective items to repair center D1 Room Repair / WH-RAT)
        if (outboundExport != null && outboundExport.Length > 0)
        {
            var wsExp = wb.Worksheets.Add("Outbound Orde Export");
            wsExp.Cell(1, 1).Value = "No";
            wsExp.Cell(1, 2).Value = "Part Number";
            wsExp.Cell(1, 3).Value = "Part Description";
            wsExp.Cell(1, 4).Value = "SERIAL_NUMBER";
            wsExp.Cell(1, 5).Value = "QTY";
            wsExp.Cell(1, 7).Value = "Customer site";
            wsExp.Cell(1, 8).Value = "Address";
            wsExp.Cell(1, 20).Value = "Case No";
            wsExp.Cell(1, 26).Value = "Inventory Status";
            int rExp = 2;
            foreach (var row in outboundExport)
            {
                wsExp.Cell(rExp, 1).Value = rExp - 1;
                wsExp.Cell(rExp, 2).Value = row.PartNo;
                wsExp.Cell(rExp, 4).Value = row.Serial;
                wsExp.Cell(rExp, 5).Value = row.Qty;
                wsExp.Cell(rExp, 7).Value = row.CustSite;
                wsExp.Cell(rExp, 8).Value = "SVOA ราษฎร์บูรณะ เลขที่ 131 ถนนราษฎร์บูรณะ";
                wsExp.Cell(rExp, 20).Value = row.CaseNo;
                wsExp.Cell(rExp, 26).Value = "BAD";
                rExp++;
            }
        }

        // Inbound normal sheet
        var wsIn = wb.Worksheets.Add("Inbound normal");
        wsIn.Cell(1, 1).Value = "No";
        wsIn.Cell(1, 3).Value = "Part Number";
        wsIn.Cell(1, 4).Value = "Part Description";
        wsIn.Cell(1, 5).Value = "SERIAL_NUMBER";
        wsIn.Cell(1, 6).Value = "QTY";
        wsIn.Cell(1, 7).Value = "INVENTORY STATUS";
        wsIn.Cell(1, 16).Value = "Shipped from";
        r = 2;
        foreach (var row in inboundNormal)
        {
            wsIn.Cell(r, 1).Value = r - 1;
            wsIn.Cell(r, 3).Value = row.PartNo;
            wsIn.Cell(r, 5).Value = row.Serial;
            wsIn.Cell(r, 6).Value = row.Qty;
            wsIn.Cell(r, 7).Value = "GOOD";
            wsIn.Cell(r, 16).Value = "D1 Room Repair";
            r++;
        }

        // Minimum stock sheet
        var wsMin = wb.Worksheets.Add("Minimum Stock");
        wsMin.Cell(1, 1).Value = "No.";
        wsMin.Cell(1, 2).Value = "Part Number";
        wsMin.Cell(1, 3).Value = "Part Description";
        wsMin.Cell(1, 4).Value = "AVAILABLE_QTY";
        wsMin.Cell(1, 7).Value = "MIN QTY";
        wsMin.Cell(1, 8).Value = "STOCK";
        r = 2;
        foreach (var row in minStock)
        {
            wsMin.Cell(r, 1).Value = r - 1;
            wsMin.Cell(r, 2).Value = row.PartNo;
            wsMin.Cell(r, 4).Value = row.AvailQty;
            wsMin.Cell(r, 7).Value = row.MinQty;
            wsMin.Cell(r, 8).Value = row.AvailQty - row.MinQty;
            r++;
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "daily-report-multi.xlsx") { Headers = new HeaderDictionary(), ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
    }

    [Fact]
    public void Confirm_OutboundOrdeExport_MovesUnitLocationToRatchaburanaAndTransfersStock()
    {
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var part = context.Parts.First(p => p.PartNo == PartNo);
        var ratWh = context.Locations.First(l => l.Code == "WH-RAT");

        // Initial state: Unit is at DHL-BKK in InRepair
        var unit = new PartUnit
        {
            PartId = part.Id,
            SerialNo = "SN-EXP-01",
            Status = "InRepair",
            Condition = "Bad",
            LocationId = mainWh.Id
        };
        context.PartUnits.Add(unit);
        var mainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        mainStock.RepairQty = 5;
        var ratStock = context.PartStocks.First(s => s.LocationId == ratWh.Id);
        ratStock.RepairQty = 0;
        context.SaveChanges();

        // DHL sends unit to D1 Room Repair (SVOA ราษฎร์บูรณะ)
        var file = BuildMultiSheetReportFile(
            outbound: Array.Empty<(string, string, int, string, string)>(),
            inboundNormal: Array.Empty<(string, string, int)>(),
            minStock: Array.Empty<(string, int, int)>(),
            outboundExport: new[] { (PartNo, "SN-EXP-01", 1, "EXPORT-GRGLOT35-2026", "D1 Room Repair") }
        );

        var result = dailyReport.Confirm(file);
        Assert.IsType<OkObjectResult>(result);

        context.Entry(mainStock).Reload();
        context.Entry(ratStock).Reload();
        context.Entry(unit).Reload();

        // 1. PartUnit location should move to WH-RAT (Ratchaburana Warehouse)
        Assert.Equal(ratWh.Id, unit.LocationId);
        Assert.Equal("InRepair", unit.Status);
        Assert.Equal("Bad", unit.Condition);

        // 2. Stock should transfer from DHL-BKK to WH-RAT
        Assert.Equal(4, mainStock.RepairQty);
        Assert.Equal(1, ratStock.RepairQty);

        // 3. Complete the loop: Unit is repaired and returns in Inbound normal
        var returnFile = BuildMultiSheetReportFile(
            outbound: Array.Empty<(string, string, int, string, string)>(),
            inboundNormal: new[] { (PartNo, "SN-EXP-01", 1) },
            minStock: Array.Empty<(string, int, int)>()
        );

        var returnResult = dailyReport.Confirm(returnFile);
        Assert.IsType<OkObjectResult>(returnResult);

        context.Entry(mainStock).Reload();
        context.Entry(ratStock).Reload();
        context.Entry(unit).Reload();

        // Repaired unit should be back at DHL-BKK in Good condition, RepairQty deducted from WH-RAT
        Assert.Equal(mainWh.Id, unit.LocationId);
        Assert.Equal("InStock", unit.Status);
        Assert.Equal("Good", unit.Condition);
        Assert.Equal(0, ratStock.RepairQty);
        Assert.Equal(101, mainStock.GoodQty);
    }

    [Fact]
    public void Confirm_OutboundOrder_MatchesWithdrawTicket_MovesStockToTechAndIssuesPartUnit()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        tickets.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "OUT-1", TechName = "Tech Somchai" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "OUT-1");
        tickets.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto { Lines = new() { new LineDto { PartNo = PartNo, Quantity = 1 } }, Address = "Site A" });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        tickets.ApproveBatch(ticket.TicketId, batch.WithdrawBatchId);
        tickets.SendEmailConfirmedBatch(ticket.TicketId, batch.WithdrawBatchId);

        var initialMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        var initialTechStock = context.PartStocks.FirstOrDefault(s => s.LocationId == techLoc.Id)?.GoodQty ?? 0;

        var file = BuildOutboundDailyReportFile((PartNo, "Test Part", "SN-OUT-001", 1, "Tech Somchai", "OUT-1"));
        var result = dailyReport.Confirm(file);

        Assert.IsType<OkObjectResult>(result);
        var finalMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        var finalTechStock = context.PartStocks.First(s => s.LocationId == techLoc.Id).GoodQty;

        // Stock was already deducted when ApproveBatch ran (100 -> 99).
        // Confirming Outbound Order records S/N and moves stock to tech, without deducting main warehouse a second time.
        Assert.Equal(initialMainStock, finalMainStock);
        Assert.Equal(initialTechStock + 1, finalTechStock);

        var unit = context.PartUnits.FirstOrDefault(u => u.SerialNo == "SN-OUT-001");
        Assert.NotNull(unit);
        Assert.Equal("Issued", unit.Status);
        Assert.Equal(techLoc.Id, unit.LocationId);

        var withdrawBatch = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId);
        Assert.Equal("เดินทาง", withdrawBatch.Status);
    }

    [Fact]
    public void Confirm_InboundNormal_RepairedUnit_ClosesRepairLoop()
    {
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var part = context.Parts.First(p => p.PartNo == PartNo);

        // Put a unit InRepair with stock
        var unit = new PartUnit { PartId = part.Id, SerialNo = "SN-REP-01", Status = "InRepair", Condition = "Bad", LocationId = mainWh.Id };
        context.PartUnits.Add(unit);
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        stock.RepairQty = 5;
        stock.GoodQty = 50;
        context.SaveChanges();

        var file = BuildMultiSheetReportFile(
            outbound: Array.Empty<(string, string, int, string, string)>(),
            inboundNormal: new[] { (PartNo, "SN-REP-01", 1) },
            minStock: Array.Empty<(string, int, int)>()
        );

        var result = dailyReport.Confirm(file);
        Assert.IsType<OkObjectResult>(result);

        context.Entry(stock).Reload();
        context.Entry(unit).Reload();

        Assert.Equal(4, stock.RepairQty);
        Assert.Equal(51, stock.GoodQty);
        Assert.Equal("InStock", unit.Status);
        Assert.Equal("Good", unit.Condition);
    }

    [Fact]
    public void Preview_WithMinimumStock_CalculatesReconciliationAudit()
    {
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        stock.GoodQty = 100; // system has 100
        context.SaveChanges();

        // DHL has 95 in file -> difference of +5
        var file = BuildMultiSheetReportFile(
            outbound: Array.Empty<(string, string, int, string, string)>(),
            inboundNormal: Array.Empty<(string, string, int)>(),
            minStock: new[] { (PartNo, 95, 20) }
        );

        var result = dailyReport.Preview(file);
        var ok = Assert.IsType<OkObjectResult>(result);

        var reconciliation = (List<DailyReportController.ReconciliationItem>)ok.Value!.GetType().GetProperty("reconciliation")!.GetValue(ok.Value)!;
        Assert.Single(reconciliation);
        Assert.Equal(PartNo, reconciliation[0].PartNo);
        Assert.Equal(95, reconciliation[0].DhlAvailableQty);
        Assert.Equal(100, reconciliation[0].SystemGoodQty);
        Assert.Equal(5, reconciliation[0].DiffGood);
        Assert.Equal("DIFF", reconciliation[0].Status);
    }

    [Fact]
    public void Confirm_OutboundOrder_UnmatchedTicket_AutoCreatesTicketAndBatchAndStockMovement()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        context.FeContacts.Add(new FeContact { FeName = "Tech Somchai", FeId = "FE-007", Address = "Site 123" });
        context.SaveChanges();

        var initialMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        var initialTechStock = context.PartStocks.FirstOrDefault(s => s.LocationId == techLoc.Id)?.GoodQty ?? 0;

        var file = BuildOutboundDailyReportFile((PartNo, "Test Part", "SN-AUTO-01", 1, "Tech Somchai", "CASE-NEW-999"));
        var result = dailyReport.Confirm(file);
        Assert.IsType<OkObjectResult>(result);

        // Verify Ticket created
        var autoTicket = context.Tickets.FirstOrDefault(t => t.ExternalTicketNo == "CASE-NEW-999");
        Assert.NotNull(autoTicket);
        Assert.Equal("Tech Somchai", autoTicket.TechName);

        // Verify WithdrawBatch created (starts in เดินทาง if FE Receive Date is not yet filled)
        var autoBatch = context.WithdrawBatches.FirstOrDefault(b => b.TicketId == autoTicket.TicketId);
        Assert.NotNull(autoBatch);
        Assert.Equal("เดินทาง", autoBatch.Status);
        Assert.Equal("Site 123", autoBatch.WithdrawAddress);
        Assert.StartsWith("WD-", autoBatch.WithdrawSlipNo ?? "");

        // Verify PartLine
        var partLine = context.TicketPartLines.FirstOrDefault(l => l.WithdrawBatchId == autoBatch.WithdrawBatchId);
        Assert.NotNull(partLine);
        Assert.Equal("SN-AUTO-01", partLine.SerialNo);
        Assert.Equal(1, partLine.ConfirmedQty);

        // Verify Stock adjustments
        var finalMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        var finalTechStock = context.PartStocks.First(s => s.LocationId == techLoc.Id).GoodQty;
        Assert.Equal(initialMainStock - 1, finalMainStock);
        Assert.Equal(initialTechStock + 1, finalTechStock);

        // Verify PartUnit status
        var unit = context.PartUnits.FirstOrDefault(u => u.SerialNo == "SN-AUTO-01");
        Assert.NotNull(unit);
        Assert.Equal("Issued", unit.Status);
        Assert.Equal(techLoc.Id, unit.LocationId);
    }

    [Fact]
    public void UndoRow_OutboundRow_RevertsStockAndRestoresPartUnit()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        var initialMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;

        var file = BuildOutboundDailyReportFile((PartNo, "Test Part", "SN-UNDO-01", 1, "Tech Somchai", ""));
        var result = dailyReport.Confirm(file);
        Assert.IsType<OkObjectResult>(result);

        var afterConfirmMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        Assert.Equal(initialMainStock - 1, afterConfirmMainStock);

        var row = context.DailyReportImportRows.First(r => r.SerialNo == "SN-UNDO-01");
        var undoResult = dailyReport.UndoRow(row.Id);
        Assert.IsType<OkObjectResult>(undoResult);

        var afterUndoMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        Assert.Equal(initialMainStock, afterUndoMainStock);

        var unit = context.PartUnits.First(u => u.SerialNo == "SN-UNDO-01");
        Assert.Equal("InStock", unit.Status);
        Assert.Equal(mainWh.Id, unit.LocationId);
    }

    [Fact]
    public void Confirm_OutboundOrder_MultipleRowsSameCaseNo_ReusesSameTicketAndBatch()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        var part2 = new Part { PartNo = "DR-TEST-PART2", PartName = "Part 2", IsActive = true };
        context.Parts.Add(part2);
        context.PartStocks.Add(new PartStock { PartId = part2.Id, LocationId = mainWh.Id, GoodQty = 50, BadQty = 0 });
        context.SaveChanges();

        var part1 = context.Parts.First(p => p.PartNo == PartNo);
        var initialMainStock1 = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part1.Id).GoodQty;
        var initialMainStock2 = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part2.Id).GoodQty;

        var file = BuildOutboundDailyReportFile(
            (PartNo, "Test Part", "SN-SAME-01", 1, "Tech Somchai", "CASE-SHARED-101"),
            ("DR-TEST-PART2", "Part 2", "SN-SAME-02", 1, "Tech Somchai", "CASE-SHARED-101")
        );
        var result = dailyReport.Confirm(file);
        Assert.IsType<OkObjectResult>(result);

        // Only ONE ticket should be created for CASE-SHARED-101
        var matchingTickets = context.Tickets.Where(t => t.ExternalTicketNo == "CASE-SHARED-101").ToList();
        Assert.Single(matchingTickets);

        // Only ONE withdraw batch under that ticket
        var batches = context.WithdrawBatches.Where(b => b.TicketId == matchingTickets[0].TicketId).ToList();
        Assert.Single(batches);
        Assert.True(batches[0].Status == "เบิก" || batches[0].Status == "เดินทาง");

        // Two part lines under this batch
        var lines = context.TicketPartLines.Where(l => l.WithdrawBatchId == batches[0].WithdrawBatchId).ToList();
        Assert.Equal(2, lines.Count);

        // Both stocks deducted
        var finalStock1 = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part1.Id).GoodQty;
        var finalStock2 = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part2.Id).GoodQty;
        Assert.Equal(initialMainStock1 - 1, finalStock1);
        Assert.Equal(initialMainStock2 - 1, finalStock2);

        // Both PartUnits created as Issued
        var u1 = context.PartUnits.FirstOrDefault(u => u.SerialNo == "SN-SAME-01");
        var u2 = context.PartUnits.FirstOrDefault(u => u.SerialNo == "SN-SAME-02");
        Assert.NotNull(u1);
        Assert.Equal("Issued", u1.Status);
        Assert.NotNull(u2);
        Assert.Equal("Issued", u2.Status);
    }

    [Fact]
    public void Confirm_OutboundOrder_BlankCaseNo_GeneratesAutoTicketNumber()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        var initialMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;

        var file = BuildOutboundDailyReportFile((PartNo, "Test Part", "SN-BLANK-01", 1, "Tech Somchai", ""));
        var result = dailyReport.Confirm(file);
        Assert.IsType<OkObjectResult>(result);

        var unit = context.PartUnits.FirstOrDefault(u => u.SerialNo == "SN-BLANK-01");
        Assert.NotNull(unit);
        Assert.Equal("Issued", unit.Status);

        var autoTicket = context.Tickets.FirstOrDefault(t => t.ExternalTicketNo.StartsWith("AUTO-"));
        Assert.NotNull(autoTicket);

        var finalMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        Assert.Equal(initialMainStock - 1, finalMainStock);
    }

    [Fact]
    public void Confirm_OutboundOrder_AlreadyIssuedSerial_DoesNotDoubleCount()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        var initialMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;

        var file1 = BuildOutboundDailyReportFile((PartNo, "Test Part", "SN-REIMPORT-01", 1, "Tech Somchai", "CASE-111"));
        var result1 = dailyReport.Confirm(file1);
        Assert.IsType<OkObjectResult>(result1);

        var stockAfterFirst = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        Assert.Equal(initialMainStock - 1, stockAfterFirst);

        // Re-confirming the same file
        var file2 = BuildOutboundDailyReportFile((PartNo, "Test Part", "SN-REIMPORT-01", 1, "Tech Somchai", "CASE-111"));
        var result2 = dailyReport.Confirm(file2);
        var ok = Assert.IsType<OkObjectResult>(result2);

        var stockAfterSecond = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        Assert.Equal(stockAfterFirst, stockAfterSecond); // Stock remains unchanged!

        var rows = (List<DailyReportController.RowResult>)ok.Value!.GetType().GetProperty("rows")!.GetValue(ok.Value)!;
        Assert.Single(rows);
        Assert.Equal("AlreadyImported", rows[0].MatchType);
    }

    [Fact]
    public void Confirm_PriorToBaseline_Outbound_RegistersPartUnitIssued_WithoutDeductingStock()
    {
        var (tickets, dailyReport, context, mainWh, techLoc) = Create();
        var initialMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;

        // Date is 2026-09-02 (before baseline snapshot of 2026-09-03)
        var priorDate = new DateTime(2026, 9, 2);
        var file = BuildOutboundDailyReportFileWithDate((PartNo, "Test Part", "SN-BASELINE-01", 1, "Tech Somchai", "CASE-OLD-01", priorDate));
        var result = dailyReport.Confirm(file);
        var ok = Assert.IsType<OkObjectResult>(result);

        var rows = (List<DailyReportController.RowResult>)ok.Value!.GetType().GetProperty("rows")!.GetValue(ok.Value)!;
        Assert.Single(rows);
        Assert.Equal("OutboundAutoTicket", rows[0].MatchType);

        // Stock count must NOT change in main warehouse (exempt from double deduction)
        var finalMainStock = context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty;
        Assert.Equal(initialMainStock, finalMainStock);

        // But Serial Number MUST be registered as Issued in PartUnits
        var unit = context.PartUnits.FirstOrDefault(u => u.SerialNo == "SN-BASELINE-01");
        Assert.NotNull(unit);
        Assert.Equal("Issued", unit.Status);
        Assert.Equal(techLoc.Id, unit.LocationId);

        // Ticket and WithdrawBatch are created
        var ticket = context.Tickets.FirstOrDefault(t => t.ExternalTicketNo == "CASE-OLD-01");
        Assert.NotNull(ticket);
    }

    private class FakeEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = System.IO.Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
    }
}
