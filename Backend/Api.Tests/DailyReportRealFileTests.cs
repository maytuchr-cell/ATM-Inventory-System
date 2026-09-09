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

using Xunit.Abstractions;

namespace Api.Tests;

public class DailyReportRealFileTests
{
    private readonly ITestOutputHelper _output;

    public DailyReportRealFileTests(ITestOutputHelper output)
    {
        _output = output;
    }
    private static string FindDocumentFile(string fileName)
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            var target = Path.Combine(current, "Document", fileName);
            if (File.Exists(target)) return target;
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }
        throw new FileNotFoundException($"Could not find Document/{fileName}");
    }

    private static FileStream OpenFileShared(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    private static XLWorkbook OpenWorkbookShared(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new XLWorkbook(stream);
    }

    [Fact]
    public void Preview_Real0405SepReport_ParsesMultiSheetsAndReconciliation()
    {
        var filePath = FindDocumentFile("Dataone Daily Report 04-05 Sep 2026_.xlsx");
        Assert.True(File.Exists(filePath));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var mainWh = new Location { Code = "DHL-BKK", Name = "DHL Central", LocationType = "DHL_CENTER", IsActive = true };
        var techLoc = new Location { Code = "OL-TECH", Name = "Technician", LocationType = "OL_TECHNICIAN", IsActive = true };
        context.Locations.AddRange(mainWh, techLoc);
        context.SaveChanges();

        var stock = new StockService(context);
        var audit = new AuditService(context);
        var controller = new DailyReportController(context, stock, audit);

        using var stream = OpenFileShared(filePath);
        var file = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(filePath))
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        var result = controller.Preview(file);
        var ok = Assert.IsType<OkObjectResult>(result);

        var rows = (List<DailyReportController.RowResult>)ok.Value!.GetType().GetProperty("rows")!.GetValue(ok.Value)!;
        var summary = (DailyReportController.Summary)ok.Value!.GetType().GetProperty("summary")!.GetValue(ok.Value)!;
        var reconciliation = (List<DailyReportController.ReconciliationItem>)ok.Value!.GetType().GetProperty("reconciliation")!.GetValue(ok.Value)!;

        Assert.True(rows.Count > 0, "Should parse rows from file");
        Assert.True(reconciliation.Count > 0, "Should parse Minimum Stock for reconciliation");
        Assert.True(summary.OutboundCount > 0 || summary.PriorToBaselineCount > 0, "Should have outbound or baseline rows");
    }

    [Fact]
    public void Preview_Real07SepReport_ParsesMultiSheetsAndReconciliation()
    {
        var filePath = FindDocumentFile("Dataone Daily Report 07 Sep 2026_.xlsx");
        Assert.True(File.Exists(filePath));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var mainWh = new Location { Code = "DHL-BKK", Name = "DHL Central", LocationType = "DHL_CENTER", IsActive = true };
        var techLoc = new Location { Code = "OL-TECH", Name = "Technician", LocationType = "OL_TECHNICIAN", IsActive = true };
        context.Locations.AddRange(mainWh, techLoc);
        context.SaveChanges();

        var stock = new StockService(context);
        var audit = new AuditService(context);
        var controller = new DailyReportController(context, stock, audit);

        using var stream = OpenFileShared(filePath);
        var file = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(filePath))
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        var result = controller.Preview(file);
        var ok = Assert.IsType<OkObjectResult>(result);

        var rows = (List<DailyReportController.RowResult>)ok.Value!.GetType().GetProperty("rows")!.GetValue(ok.Value)!;
        var summary = (DailyReportController.Summary)ok.Value!.GetType().GetProperty("summary")!.GetValue(ok.Value)!;
        var reconciliation = (List<DailyReportController.ReconciliationItem>)ok.Value!.GetType().GetProperty("reconciliation")!.GetValue(ok.Value)!;

        Assert.True(rows.Count > 0, "Should parse rows from 07 Sep file");
        Assert.True(reconciliation.Count > 0, "Should parse Minimum Stock from 07 Sep file");
        Assert.True(summary.TotalProcessed > 0, "Summary should count processed rows");
    }

    [Fact]
    public void Inspect07SepReport()
    {
        var filePath07 = FindDocumentFile("Dataone Daily Report 07 Sep 2026_.xlsx");
        var filePath0405 = FindDocumentFile("Dataone Daily Report 04-05 Sep 2026_.xlsx");
        using var wb07 = OpenWorkbookShared(filePath07);
        using var wb0405 = OpenWorkbookShared(filePath0405);

        _output.WriteLine("=== SHEETS IN 07 SEP FILE ===");
        _output.WriteLine("=== INSPECTION OF COLUMNS AND LOCATIONS IN EXCEL ===");
        foreach (var ws in wb07.Worksheets)
        {
            _output.WriteLine($"\n--- Sheet: '{ws.Name}' ({ws.LastRowUsed()?.RowNumber()} rows, {ws.LastColumnUsed()?.ColumnNumber()} cols) ---");
            var headers = new List<string>();
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                var h = ws.Cell(1, c).GetString().Trim().Replace("\n", " ");
                if (!string.IsNullOrEmpty(h)) headers.Add($"[Col {c}] {h}");
            }
            _output.WriteLine("Headers: " + string.Join(" | ", headers));
        }

        // Specifically inspect Return inbound columns and sample data
        var retWs = wb07.Worksheet("Return inbound");
        _output.WriteLine("\n=== RETURN INBOUND 07 SEP SAMPLE DATA (FIRST 5 ROWS) ===");
        var retCols = retWs.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int r = 1; r <= Math.Min(6, retWs.LastRowUsed()?.RowNumber() ?? 1); r++)
        {
            var rowVals = new List<string>();
            for (int c = 1; c <= retCols; c++)
            {
                var val = retWs.Cell(r, c).GetString().Trim().Replace("\n", " ");
                if (!string.IsNullOrEmpty(val)) rowVals.Add($"[C{c}:{retWs.Cell(1, c).GetString().Trim()}]={val}");
            }
            _output.WriteLine($"Row {r}: " + string.Join(" | ", rowVals));
        }

        // Search specific sheets for any mention of repair centers/destinations
        var sheetsToCheck = new[] { "Return inbound", "Booking Return", "ReturnWH", "Inbound normal", "Hold Receive-Export" };
        foreach (var sName in sheetsToCheck)
        {
            if (!wb07.Worksheets.Contains(sName)) continue;
            var ws = wb07.Worksheet(sName);
            var headerList = new List<string>();
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                var h = ws.Cell(1, c).GetString().Trim().Replace("\n", " ");
                if (!string.IsNullOrEmpty(h)) headerList.Add($"C{c}:{h}");
            }
            _output.WriteLine($"\n[Sheet: {sName}] Headers: {string.Join(" | ", headerList)}");

            // Look for any cells containing "ซ่อม", "repair", "lab", "grg", "svoa", "d1"
            var hits = new List<string>();
            var lastRow = Math.Min(100, ws.LastRowUsed()?.RowNumber() ?? 1);
            for (int r = 1; r <= lastRow; r++)
            {
                for (int c = 1; c <= lastCol; c++)
                {
                    var txt = ws.Cell(r, c).GetString().Trim();
                    if (txt.IndexOf("ซ่อม", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.IndexOf("repair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.IndexOf("lab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.IndexOf("svoa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.IndexOf("grg", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (hits.Count < 10)
                            hits.Add($"Row {r}, Col {c} ({ws.Cell(1, c).GetString().Trim()}): '{txt}'");
                    }
                }
            }
            _output.WriteLine($"  Found {hits.Count} sample repair keyword hits in first 100 rows:");
            foreach (var hit in hits) _output.WriteLine($"    {hit}");
        }

        // Inspect Outbound Orde Export in detail
        if (wb07.Worksheets.Contains("Outbound Orde Export"))
        {
            var expWs = wb07.Worksheet("Outbound Orde Export");
            _output.WriteLine("\n=== OUTBOUND ORDE EXPORT 07 SEP DETAILS ===");
            _output.WriteLine($"Total Rows: {expWs.LastRowUsed()?.RowNumber()}");
            var custSites = new HashSet<string>();
            var addresses = new HashSet<string>();
            var orderTypes = new HashSet<string>();
            var invStatuses = new HashSet<string>();
            for (int r = 2; r <= (expWs.LastRowUsed()?.RowNumber() ?? 1); r++)
            {
                var cs = expWs.Cell(r, 7).GetString().Trim();
                if (!string.IsNullOrEmpty(cs)) custSites.Add(cs);
                var ad = expWs.Cell(r, 8).GetString().Trim();
                if (!string.IsNullOrEmpty(ad)) addresses.Add(ad);
                var ot = expWs.Cell(r, 21).GetString().Trim();
                if (!string.IsNullOrEmpty(ot)) orderTypes.Add(ot);
                var st = expWs.Cell(r, 26).GetString().Trim();
                if (!string.IsNullOrEmpty(st)) invStatuses.Add(st);
            }
            _output.WriteLine("Customer Sites in Export: " + string.Join(", ", custSites));
            _output.WriteLine("Addresses in Export: " + string.Join(", ", addresses.Take(5)));
            _output.WriteLine("Order Types in Export: " + string.Join(", ", orderTypes));
            _output.WriteLine("Inventory Statuses in Export: " + string.Join(", ", invStatuses));

            // Print first 3 rows
            for (int r = 1; r <= 4; r++)
            {
                var rowVals = new List<string>();
                for (int c = 1; c <= 28; c++)
                {
                    var val = expWs.Cell(r, c).GetString().Trim().Replace("\n", " ");
                    if (!string.IsNullOrEmpty(val)) rowVals.Add($"[{expWs.Cell(1, c).GetString().Trim()}]={val}");
                }
                _output.WriteLine($"Row {r}: " + string.Join(" | ", rowVals));
            }
        }








        var out07 = wb07.Worksheet("Outbound Order ");
        var ret07 = wb07.Worksheet("Return inbound");

        // Compare Outbound rows between 04-05 Sep and 07 Sep
        var out0405 = wb0405.Worksheet("Outbound Order ");
        _output.WriteLine($"Outbound rows: 04-05 Sep={out0405.LastRowUsed()?.RowNumber()}, 07 Sep={out07.LastRowUsed()?.RowNumber()}");
        var ret0405 = wb0405.Worksheet("Return inbound");
        _output.WriteLine($"Return rows: 04-05 Sep={ret0405.LastRowUsed()?.RowNumber()}, 07 Sep={ret07.LastRowUsed()?.RowNumber()}");

        // Check if all Outbound rows of 04-05 Sep are present in 07 Sep (by SerialNo and CaseNo)
        var sn0405Out = new HashSet<string>();
        for (int r = 2; r <= (out0405.LastRowUsed()?.RowNumber() ?? 2); r++)
        {
            var sn = out0405.Cell(r, 4).GetString().Trim();
            if (!string.IsNullOrEmpty(sn)) sn0405Out.Add(sn);
        }
        var sn07Out = new HashSet<string>();
        for (int r = 2; r <= (out07.LastRowUsed()?.RowNumber() ?? 2); r++)
        {
            var sn = out07.Cell(r, 4).GetString().Trim();
            if (!string.IsNullOrEmpty(sn)) sn07Out.Add(sn);
        }

        var missingOutIn07 = sn0405Out.Except(sn07Out).ToList();
        _output.WriteLine($"Distinct Outbound SNs: 04-05={sn0405Out.Count}, 07={sn07Out.Count}. Missing in 07: {missingOutIn07.Count}");

        // Check if all Return rows of 04-05 Sep are present in 07 Sep
        var sn0405Ret = new HashSet<string>();
        for (int r = 2; r <= (ret0405.LastRowUsed()?.RowNumber() ?? 2); r++)
        {
            var sn = ret0405.Cell(r, 5).GetString().Trim();
            if (!string.IsNullOrEmpty(sn)) sn0405Ret.Add(sn);
        }
        var sn07Ret = new HashSet<string>();
        for (int r = 2; r <= (ret07.LastRowUsed()?.RowNumber() ?? 2); r++)
        {
            var sn = ret07.Cell(r, 5).GetString().Trim();
            if (!string.IsNullOrEmpty(sn)) sn07Ret.Add(sn);
        }
        var missingRetIn07 = sn0405Ret.Except(sn07Ret).ToList();
        _output.WriteLine($"Distinct Return SNs: 04-05={sn0405Ret.Count}, 07={sn07Ret.Count}. Missing in 07: {missingRetIn07.Count}");
    }

    [Fact]
    public void TestDirect07SepImportVsSequential()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "Backend", "Api", "AtmInventory.db")))
        {
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }
        var realDbPath = Path.Combine(current!, "Backend", "Api", "AtmInventory.db");
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"sim_07_{Guid.NewGuid():N}.db");
        File.Copy(realDbPath, tempDbPath, true);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={tempDbPath}")
                .Options;

            using var context = new AppDbContext(options);
            var stock = new StockService(context);
            var audit = new AuditService(context);
            var controller = new DailyReportController(context, stock, audit);

            var filePath07 = FindDocumentFile("Dataone Daily Report 07 Sep 2026_.xlsx");
            using var stream = OpenFileShared(filePath07);
            var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(filePath07))
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };

            // Test Preview of 07 Sep directly from baseline 03 Sep
            var previewRes = controller.Preview(formFile);
            var okPreview = Assert.IsType<OkObjectResult>(previewRes);
            var summary = (DailyReportController.Summary)okPreview.Value!.GetType().GetProperty("summary")!.GetValue(okPreview.Value)!;
            var rows = (List<DailyReportController.RowResult>)okPreview.Value!.GetType().GetProperty("rows")!.GetValue(okPreview.Value)!;

            _output.WriteLine($"Direct 07 Sep Preview Summary:");
            _output.WriteLine($"  TotalProcessed: {summary.TotalProcessed}");
            _output.WriteLine($"  OutboundCount: {summary.OutboundCount}");
            _output.WriteLine($"  ReturnConfirmed: {summary.ReturnConfirmed}");
            _output.WriteLine($"  RepairCompleted: {summary.RepairCompleted}");
            _output.WriteLine($"  StillInRepair: {summary.StillInRepair}");
            _output.WriteLine($"  Unmatched: {summary.Unmatched}");
            _output.WriteLine($"  AlreadyImported: {summary.AlreadyImportedCount}");
            _output.WriteLine($"  PriorToBaseline: {summary.PriorToBaselineCount}");

            // Now test Confirm of 07 Sep directly!
            using var stream2 = OpenFileShared(filePath07);
            var formFile2 = new FormFile(stream2, 0, stream2.Length, "file", Path.GetFileName(filePath07))
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };
            var confirmRes = controller.Confirm(formFile2);
            var okConfirm = Assert.IsType<OkObjectResult>(confirmRes);
            _output.WriteLine("Direct 07 Sep Confirm SUCCEEDED with 0 errors!");

            var mainWh = context.Locations.First(l => l.Code == "DHL-BKK");
            var techLoc = context.Locations.First(l => l.Code == "OL-TECH");
            var totalMainGood = context.PartStocks.Where(s => s.LocationId == mainWh.Id).Sum(s => s.GoodQty);
            var totalMainRepair = context.PartStocks.Where(s => s.LocationId == mainWh.Id).Sum(s => s.RepairQty);
            var totalTechGood = context.PartStocks.Where(s => s.LocationId == techLoc.Id).Sum(s => s.GoodQty);
            var totalTickets = context.Tickets.Count();
            var totalBatches = context.WithdrawBatches.Count();
            _output.WriteLine($"State after DIRECT 07 Sep Confirm:");
            _output.WriteLine($"  DHL-BKK Good={totalMainGood}, Repair={totalMainRepair}");
            _output.WriteLine($"  OL-TECH Good={totalTechGood}");
            _output.WriteLine($"  Tickets={totalTickets}, WithdrawBatches={totalBatches}");

            // Now let's test SEQUENTIAL: 04-05 Sep FIRST, then 07 Sep on another temp DB!
            var tempDbSeqPath = Path.Combine(Path.GetTempPath(), $"sim_seq_{Guid.NewGuid():N}.db");
            File.Copy(realDbPath, tempDbSeqPath, true);
            try
            {
                var optionsSeq = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={tempDbSeqPath}")
                    .Options;
                using var contextSeq = new AppDbContext(optionsSeq);
                var stockSeq = new StockService(contextSeq);
                var auditSeq = new AuditService(contextSeq);
                var controllerSeq = new DailyReportController(contextSeq, stockSeq, auditSeq);

                var filePath0405 = FindDocumentFile("Dataone Daily Report 04-05 Sep 2026_.xlsx");
                using var stream0405 = OpenFileShared(filePath0405);
                var formFile0405 = new FormFile(stream0405, 0, stream0405.Length, "file", Path.GetFileName(filePath0405))
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                };
                var confirm0405 = controllerSeq.Confirm(formFile0405);
                Assert.IsType<OkObjectResult>(confirm0405);
                _output.WriteLine("Step 1: 04-05 Sep Confirm SUCCEEDED!");

                using var stream07Seq = OpenFileShared(filePath07);
                var formFile07Seq = new FormFile(stream07Seq, 0, stream07Seq.Length, "file", Path.GetFileName(filePath07))
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                };
                var confirm07Seq = controllerSeq.Confirm(formFile07Seq);
                Assert.IsType<OkObjectResult>(confirm07Seq);
                _output.WriteLine("Step 2: 07 Sep Confirm SUCCEEDED after 04-05 Sep!");

                var mainWhSeq = contextSeq.Locations.First(l => l.Code == "DHL-BKK");
                var techLocSeq = contextSeq.Locations.First(l => l.Code == "OL-TECH");
                var totalMainGoodSeq = contextSeq.PartStocks.Where(s => s.LocationId == mainWhSeq.Id).Sum(s => s.GoodQty);
                var totalMainRepairSeq = contextSeq.PartStocks.Where(s => s.LocationId == mainWhSeq.Id).Sum(s => s.RepairQty);
                var totalTechGoodSeq = contextSeq.PartStocks.Where(s => s.LocationId == techLocSeq.Id).Sum(s => s.GoodQty);
                var totalTicketsSeq = contextSeq.Tickets.Count();
                var totalBatchesSeq = contextSeq.WithdrawBatches.Count();
                _output.WriteLine($"State after SEQUENTIAL (04-05 -> 07) Confirm:");
                _output.WriteLine($"  DHL-BKK Good={totalMainGoodSeq}, Repair={totalMainRepairSeq}");
                _output.WriteLine($"  OL-TECH Good={totalTechGoodSeq}");
                _output.WriteLine($"  Tickets={totalTicketsSeq}, WithdrawBatches={totalBatchesSeq}");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(tempDbSeqPath))
                {
                    try { File.Delete(tempDbSeqPath); } catch { }
                }
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { }
            }
        }
    }

    [Fact]
    public void InspectRealDatabase()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "Backend", "Api", "AtmInventory.db")))
        {
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }
        var dbPath = Path.Combine(current!, "Backend", "Api", "AtmInventory.db");
        Assert.True(File.Exists(dbPath), $"AtmInventory.db should exist at {dbPath}");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var context = new AppDbContext(options);

        var batches = context.DailyReportImportBatches.ToList();
        _output.WriteLine($"=== DailyReportImportBatches ({batches.Count}) ===");
        foreach (var b in batches)
        {
            _output.WriteLine($"Batch {b.Id}: File={b.FileName}, Date={b.ImportedAt:yyyy-MM-dd HH:mm:ss}, TotalRows={b.TotalRows}, Outbound={b.OutboundCount}, ReturnConfirmed={b.ReturnConfirmedCount}, Unmatched={b.UnmatchedCount}");
        }

        var tickets = context.Tickets.ToList();
        _output.WriteLine($"=== Tickets ({tickets.Count}) ===");
        if (tickets.Any())
        {
            var minDate = tickets.Min(t => t.CreatedAt);
            var maxDate = tickets.Max(t => t.CreatedAt);
            _output.WriteLine($"Ticket CreatedAt range: {minDate:yyyy-MM-dd HH:mm:ss} to {maxDate:yyyy-MM-dd HH:mm:ss}");
            var ticketSample = tickets.Take(5).Select(t => $"{t.TicketId}:{t.ExternalTicketNo}({t.TechName})");
            _output.WriteLine($"Sample Tickets: {string.Join(", ", ticketSample)}");
        }

        var withdrawBatches = context.WithdrawBatches.ToList();
        _output.WriteLine($"=== WithdrawBatches ({withdrawBatches.Count}) ===");
        if (withdrawBatches.Any())
        {
            var statusCounts = withdrawBatches.GroupBy(b => b.Status ?? "NULL").Select(g => $"{g.Key}={g.Count()}");
            _output.WriteLine($"Batch Statuses: {string.Join(", ", statusCounts)}");
            var minWdDate = withdrawBatches.Min(b => b.WithdrawDate);
            var maxWdDate = withdrawBatches.Max(b => b.WithdrawDate);
            _output.WriteLine($"WithdrawDate range: {minWdDate:yyyy-MM-dd} to {maxWdDate:yyyy-MM-dd}");
        }

        var partLines = context.TicketPartLines.ToList();
        _output.WriteLine($"=== TicketPartLines ({partLines.Count}) ===");
        var lineTypes = partLines.GroupBy(l => l.LineType).Select(g => $"{g.Key}={g.Count()}");
        _output.WriteLine($"LineTypes: {string.Join(", ", lineTypes)}");

        var partUnits = context.PartUnits.ToList();
        _output.WriteLine($"=== PartUnits ({partUnits.Count}) ===");
        var unitStatuses = partUnits.GroupBy(u => u.Status ?? "NULL").Select(g => $"{g.Key}={g.Count()}");
        _output.WriteLine($"PartUnit Statuses: {string.Join(", ", unitStatuses)}");

        var stocks = context.PartStocks.Include(s => s.Location).ToList();
        _output.WriteLine($"=== PartStocks ===");
        var byLoc = stocks.GroupBy(s => s.Location?.Code ?? "UNKNOWN")
            .Select(g => $"{g.Key}: Good={g.Sum(s => s.GoodQty)}, Repair={g.Sum(s => s.RepairQty)}");
        foreach (var l in byLoc) _output.WriteLine($"  {l}");

        var movements = context.StockMovements.ToList();
        _output.WriteLine($"=== StockMovements ({movements.Count}) ===");
        if (movements.Any())
        {
            var movTypes = movements.GroupBy(m => m.MovementType).Select(g => $"{g.Key}={g.Count()}");
            _output.WriteLine($"Movement Types: {string.Join(", ", movTypes)}");
            var minMov = movements.Min(m => m.Timestamp);
            var maxMov = movements.Max(m => m.Timestamp);
            _output.WriteLine($"Movements Date range: {minMov:yyyy-MM-dd HH:mm:ss} to {maxMov:yyyy-MM-dd HH:mm:ss}");
        }
    }

    [Fact]
    public void SimulateManualRequisitionAndConflictCheck()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "Backend", "Api", "AtmInventory.db")))
        {
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }
        var realDbPath = Path.Combine(current!, "Backend", "Api", "AtmInventory.db");
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"sim_test_{Guid.NewGuid():N}.db");
        File.Copy(realDbPath, tempDbPath, true);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={tempDbPath}")
                .Options;

            using var context = new AppDbContext(options);
            var stock = new StockService(context);
            var audit = new AuditService(context);
            var controller = new DailyReportController(context, stock, audit);

            var filePath = FindDocumentFile("Dataone Daily Report 04-05 Sep 2026_.xlsx");
            using var wb = OpenWorkbookShared(filePath);

            var outSheet = wb.Worksheet("Outbound Order ");
            var headers = new List<string>();
            for (int c = 1; c <= 35; c++)
            {
                var h = outSheet.Cell(1, c).GetString().Trim();
                if (!string.IsNullOrEmpty(h)) headers.Add($"C{c}:{h}");
            }
            _output.WriteLine($"Outbound Headers: {string.Join(" | ", headers)}");

            var sampleOutRows = new List<(string PartNo, string CaseNo, string SerialNo, string FeName, DateTime? Date)>();
            int cPart = 0, cCase = 0, cSn = 0, cFe = 0, cDate = 0;
            for (int c = 1; c <= 35; c++)
            {
                var h = outSheet.Cell(1, c).GetString().Trim();
                if (h.Equals("Part Number", StringComparison.OrdinalIgnoreCase)) cPart = c;
                else if (h.Equals("Case No", StringComparison.OrdinalIgnoreCase)) cCase = c;
                else if (h.Contains("Serial", StringComparison.OrdinalIgnoreCase)) cSn = c;
                else if (h.Equals("FE Name", StringComparison.OrdinalIgnoreCase)) cFe = c;
                else if (h.Contains("DATE", StringComparison.OrdinalIgnoreCase)) cDate = c;
            }
            _output.WriteLine($"cPart={cPart}, cCase={cCase}, cSn={cSn}, cFe={cFe}, cDate={cDate}");

            foreach (var r in new[] { 2, 100, 300, 500, 700, 765 })
            {
                var c11 = outSheet.Cell(r, 11).GetString();
                var c31 = outSheet.Cell(r, 31).GetString();
                var c32 = outSheet.Cell(r, 32).GetString();
                var p = outSheet.Cell(r, 2).GetString();
                var c = outSheet.Cell(r, 20).GetString();
                _output.WriteLine($"Row {r}: Part={p}, Case={c}, C11(OrderDate)='{c11}', C31(ActDate)='{c31}', C32(CreationDate)='{c32}'");
            }

            // Also inspect Return inbound sheet
            var retSheet = wb.Worksheet("Return inbound");
            _output.WriteLine($"Return inbound sheet rows: {retSheet.LastRowUsed()?.RowNumber()}");

            var target1 = (PartNo: outSheet.Cell(760, 2).GetString().Trim(), CaseNo: outSheet.Cell(760, 20).GetString().Trim(), FeName: outSheet.Cell(760, 6).GetString().Trim());
            var part1 = context.Parts.FirstOrDefault(p => p.PartNo == target1.PartNo);
            Assert.NotNull(part1);

            var mainWh = context.Locations.First(l => l.Code == "DHL-BKK");
            var dhlStockBefore = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part1.Id).GoodQty;

            var ticket1 = context.Tickets.FirstOrDefault(t => t.ExternalTicketNo == target1.CaseNo);
            if (ticket1 == null)
            {
                ticket1 = new Ticket
                {
                    ExternalTicketNo = target1.CaseNo,
                    TechName = target1.FeName,
                    Status = "Open",
                    CreatedAt = DateTime.Now
                };
                context.Tickets.Add(ticket1);
                context.SaveChanges();
            }

            var batch1 = new WithdrawBatch
            {
                TicketId = ticket1.TicketId,
                WithdrawSlipNo = "WD-2026-99001",
                Status = "รอส่งเมล DHL", // Admin approved!
                WithdrawDate = DateTime.Now,
                WithdrawAddress = "Bangkok"
            };
            context.WithdrawBatches.Add(batch1);
            context.SaveChanges();

            var line1 = new TicketPartLine
            {
                TicketId = ticket1.TicketId,
                WithdrawBatchId = batch1.WithdrawBatchId,
                PartId = part1.Id,
                PartNo = target1.PartNo,
                Quantity = 1,
                ConfirmedQty = 0,
                LineType = "Withdraw"
            };
            context.TicketPartLines.Add(line1);

            // Simulate stock deduction at ApproveBatch
            stock.AdjustStock(target1.PartNo, mainWh.Id, -1, "Good", "Approve", "WithdrawBatch", batch1.WithdrawBatchId.ToString(), "admin", "Approved batch");
            context.SaveChanges();

            var dhlStockAfterApprove = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part1.Id).GoodQty;
            _output.WriteLine($"Scenario 1: Part {target1.PartNo} stock before approve={dhlStockBefore}, after approve={dhlStockAfterApprove}");

            // Scenario 2: Tech submitted requisition in system, but Admin has NOT approved yet (Status = 'รอ')
            var target2 = (PartNo: outSheet.Cell(761, 2).GetString().Trim(), CaseNo: outSheet.Cell(761, 20).GetString().Trim(), FeName: outSheet.Cell(761, 6).GetString().Trim());
            var part2 = context.Parts.FirstOrDefault(p => p.PartNo == target2.PartNo);
            var ticket2 = context.Tickets.FirstOrDefault(t => t.ExternalTicketNo == target2.CaseNo);
            if (ticket2 == null)
            {
                ticket2 = new Ticket
                {
                    ExternalTicketNo = target2.CaseNo,
                    TechName = target2.FeName,
                    Status = "Open",
                    CreatedAt = DateTime.Now
                };
                context.Tickets.Add(ticket2);
                context.SaveChanges();
            }

            var batch2 = new WithdrawBatch
            {
                TicketId = ticket2.TicketId,
                WithdrawSlipNo = "WD-2026-99002",
                Status = "รอ", // NOT approved yet!
                WithdrawDate = DateTime.Now,
                WithdrawAddress = "Chiang Mai"
            };
            context.WithdrawBatches.Add(batch2);
            context.SaveChanges();

            var line2 = new TicketPartLine
            {
                TicketId = ticket2.TicketId,
                WithdrawBatchId = batch2.WithdrawBatchId,
                PartId = part2!.Id,
                PartNo = target2.PartNo,
                Quantity = 1,
                ConfirmedQty = 0,
                LineType = "Withdraw"
            };
            context.TicketPartLines.Add(line2);
            context.SaveChanges();

            // Scenario 3: Tech created requisition with a typo in Case No (e.g. missing suffix)
            var target3 = (PartNo: outSheet.Cell(762, 2).GetString().Trim(), CaseNo: outSheet.Cell(762, 20).GetString().Trim(), FeName: outSheet.Cell(762, 6).GetString().Trim());
            var part3 = context.Parts.FirstOrDefault(p => p.PartNo == target3.PartNo);
            var ticket3 = new Ticket
            {
                ExternalTicketNo = "TYPO-CASE-999", // Typo in Case No!
                TechName = target3.FeName,
                Status = "Open",
                CreatedAt = DateTime.Now
            };
            context.Tickets.Add(ticket3);
            context.SaveChanges();

            var batch3 = new WithdrawBatch
            {
                TicketId = ticket3.TicketId,
                WithdrawSlipNo = "WD-2026-99003",
                Status = "รอส่งเมล DHL",
                WithdrawDate = DateTime.Now,
                WithdrawAddress = "Phuket"
            };
            context.WithdrawBatches.Add(batch3);
            context.SaveChanges();

            var line3 = new TicketPartLine
            {
                TicketId = ticket3.TicketId,
                WithdrawBatchId = batch3.WithdrawBatchId,
                PartId = part3!.Id,
                PartNo = target3.PartNo,
                Quantity = 1,
                ConfirmedQty = 0,
                LineType = "Withdraw"
            };
            context.TicketPartLines.Add(line3);
            stock.AdjustStock(target3.PartNo, mainWh.Id, -1, "Good", "Approve", "WithdrawBatch", batch3.WithdrawBatchId.ToString(), "admin", "Approved batch");
            context.SaveChanges();

            // Now run Preview on 04-05 Sep with this context!
            using var stream = OpenFileShared(filePath);
            var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(filePath))
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };

            var previewRes = controller.Preview(formFile);
            var ok = Assert.IsType<OkObjectResult>(previewRes);
            var rows = (List<DailyReportController.RowResult>)ok.Value!.GetType().GetProperty("rows")!.GetValue(ok.Value)!;

            _output.WriteLine($"=== PREVIEW RESULTS FOR SCENARIOS ===");
            var match1 = rows.FirstOrDefault(r => r.CaseNo == target1.CaseNo && r.PartNo == target1.PartNo);
            _output.WriteLine($"Scenario 1 (Pre-approved 'รอส่งเมล DHL', Case #{target1.CaseNo}): MatchType={match1?.MatchType}, WithdrawBatchId={match1?.WithdrawBatchId}, Note={match1?.Note}");

            var match2 = rows.FirstOrDefault(r => r.CaseNo == target2.CaseNo && r.PartNo == target2.PartNo);
            _output.WriteLine($"Scenario 2 (Pending 'รอ', Case #{target2.CaseNo}): MatchType={match2?.MatchType}, WithdrawBatchId={match2?.WithdrawBatchId}, Note={match2?.Note}");

            var match3 = rows.FirstOrDefault(r => r.CaseNo == target3.CaseNo && r.PartNo == target3.PartNo);
            _output.WriteLine($"Scenario 3 (Typo in CaseNo #{target3.CaseNo}): MatchType={match3?.MatchType}, WithdrawBatchId={match3?.WithdrawBatchId}, Note={match3?.Note}");

            // Now run Confirm on 04-05 Sep to check actual stock deduction!
            using var stream2 = OpenFileShared(filePath);
            var formFile2 = new FormFile(stream2, 0, stream2.Length, "file", Path.GetFileName(filePath))
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };

            var stockBeforeConfirm = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part1.Id).GoodQty;
            var confirmRes = controller.Confirm(formFile2);
            var okConfirm = Assert.IsType<OkObjectResult>(confirmRes);
            var stockAfterConfirm = context.PartStocks.First(s => s.LocationId == mainWh.Id && s.PartId == part1.Id).GoodQty;
            _output.WriteLine($"STOCK CHECK FOR PART {target1.PartNo}: Initial={dhlStockBefore}, AfterApprove={dhlStockAfterApprove}, AfterConfirm={stockAfterConfirm} (Delta at Confirm: {stockAfterConfirm - stockBeforeConfirm})");

            // Verify batch2 status after confirm
            var batch2After = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch2.WithdrawBatchId);
            _output.WriteLine($"Scenario 2 batch2 status after Confirm: Status={batch2After.Status}, Approver={batch2After.ApproverName}");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { }
            }
        }
    }

    [Fact]
    public void ScanAllDistinctLocationsInExcelFiles()
    {
        var files = new[]
        {
            "Dataone Daily Report 03 Sep 2026_.xlsx",
            "Dataone Daily Report 04-05 Sep 2026_.xlsx",
            "Dataone Daily Report 07 Sep 2026_.xlsx"
        };

        foreach (var fname in files)
        {
            var fpath = FindDocumentFile(fname);
            if (!File.Exists(fpath)) continue;
            _output.WriteLine($"\n================== FILE: {fname} ==================");
            using var wb = OpenWorkbookShared(fpath);
            foreach (var ws in wb.Worksheets)
            {
                var sname = ws.Name;
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                if (lastRow <= 1) continue;

                var headers = new Dictionary<int, string>();
                for (int c = 1; c <= lastCol; c++)
                {
                    var h = ws.Cell(1, c).GetString().Trim();
                    if (!string.IsNullOrEmpty(h)) headers[c] = h;
                }

                // Look for location-related columns
                var locCols = headers.Where(kvp =>
                    kvp.Value.Contains("Site", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains("Customer", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains("Location", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains("Shipped", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains("Warehouse", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains("Destination", StringComparison.OrdinalIgnoreCase)
                ).ToList();

                if (!locCols.Any()) continue;

                _output.WriteLine($"--- Sheet: '{sname}' (Rows: {lastRow}) ---");
                foreach (var lc in locCols)
                {
                    var distinctVals = new HashSet<string>();
                    for (int r = 2; r <= lastRow; r++)
                    {
                        var val = ws.Cell(r, lc.Key).GetString().Trim();
                        if (!string.IsNullOrWhiteSpace(val)) distinctVals.Add(val);
                    }
                    _output.WriteLine($"  Col '{lc.Value}' ({distinctVals.Count} distinct): {string.Join(" | ", distinctVals.Take(10))}");
                    if (distinctVals.Count > 10) _output.WriteLine($"    ... and {distinctVals.Count - 10} more");
                }
            }
        }
    }

    [Fact(Skip = "Run manually to avoid mutating production DB during test runs")]
    public void Import07SepToRealDb()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "Backend", "Api", "AtmInventory.db")))
        {
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }
        var realDbPath = Path.Combine(current!, "Backend", "Api", "AtmInventory.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={realDbPath}")
            .Options;

        using var context = new AppDbContext(options);
        var stock = new StockService(context);
        var audit = new AuditService(context);
        var controller = new DailyReportController(context, stock, audit);

        var filePath07 = FindDocumentFile("Dataone Daily Report 07 Sep 2026_.xlsx");
        using var stream = OpenFileShared(filePath07);
        var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(filePath07))
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        var result = controller.Confirm(formFile);
        var ok = Assert.IsType<OkObjectResult>(result);
        _output.WriteLine("Imported successfully to real AtmInventory.db!");
    }
}
