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
        var mainWh = new Location { Code = "WH-RAT", Name = "Ratchaburana Warehouse", LocationType = "RATCHABURANA", IsActive = true };
        var techLoc = new Location { Code = "OL-TECH", Name = "Technician Stock", LocationType = "OL_TECHNICIAN", IsActive = true };
        context.Parts.Add(part);
        context.Locations.AddRange(mainWh, techLoc);
        context.SaveChanges();
        context.PartStocks.Add(new PartStock { PartId = part.Id, LocationId = mainWh.Id, GoodQty = 100, BadQty = 0 });
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
        // silently fall back to guessing by Part No. against some unrelated open ticket.
        var (tickets, dailyReport, context, mainWh, _) = Create();
        var (ticket, batch) = CreateShippedReturnTicket(tickets, context, "DR-CASE-2");
        var file = BuildDailyReportFileWithCaseNo((PartNo, "Test Part", "SN-CASE-002", 1, "GOOD", null, "NO-SUCH-CASE"));

        var result = dailyReport.Confirm(file);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("เดินทาง", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).ReturnStatus); // untouched
        Assert.Equal(99, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // untouched
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
