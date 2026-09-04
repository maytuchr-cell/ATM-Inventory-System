using Api.Controllers;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.Tests;

/// <summary>
/// Builds a TicketController wired to a fresh EF Core InMemory database per test, seeded with
/// the parts/locations the withdraw/return flow depends on (active part, technician on-hand
/// location "OL_TECHNICIAN", main warehouse "DHL-BKK" — see TicketController.ReceiveTicket /
/// ConfirmReturnArrived). Each test gets its own database name so tests never see each other's data.
/// </summary>
public static class TicketControllerFixture
{
    public const string PartNo = "TEST-PART-001";

    public static (TicketController Controller, AppDbContext Context) Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);

        var part = new Part { PartNo = PartNo, PartName = "Test Part", IsActive = true };
        var mainWh = new Location { Code = "DHL-BKK", Name = "DHL Center Bangkok", LocationType = "DHL_CENTER", IsActive = true };
        context.Parts.Add(part);
        context.Locations.Add(new Location { Code = "OL-TECH", Name = "Technician Stock", LocationType = "OL_TECHNICIAN", IsActive = true });
        context.Locations.Add(mainWh);
        context.SaveChanges();

        // Central warehouse starts stocked so approve/receive succeed by default — tests that
        // care about the insufficient-stock path seed a smaller/zero quantity explicitly instead.
        context.PartStocks.Add(new PartStock { PartId = part.Id, LocationId = mainWh.Id, GoodQty = 100, BadQty = 0 });
        context.SaveChanges();

        var stock = new StockService(context);
        var audit = new AuditService(context);
        var config = new ConfigurationBuilder().Build();
        var env = new FakeWebHostEnvironment();

        var controller = new TicketController(context, stock, audit, config, env);
        return (controller, context);
    }

    /// <summary>
    /// Registers a second part (EquivalentPartNo) as a registered equivalent of PartNo, so
    /// TicketController.SubstitutePart accepts swapping one for the other.
    /// </summary>
    public const string EquivalentPartNo = "TEST-PART-002";

    /// <param name="equivalentStock">Good stock to seed for the equivalent part at DHL-BKK — 0
    /// (default) matches the old behavior of an equivalent with nothing on hand. Since
    /// TicketController.TryAutoApprove now immediately rejects a "รออะไหล่" batch when NO
    /// registered equivalent has enough stock to cover the shortfall, tests that need the batch to
    /// actually land on/stay at "รออะไหล่" (e.g. to then call SubstitutePart) must pass enough here.</param>
    public static void SeedEquivalentPart(AppDbContext context, int equivalentStock = 0)
    {
        var original = context.Parts.First(p => p.PartNo == PartNo);
        var equivalent = new Part { PartNo = EquivalentPartNo, PartName = "Test Equivalent Part", IsActive = true };
        context.Parts.Add(equivalent);
        context.SaveChanges();

        var group = new EquivalentGroup { Name = "Test Group" };
        context.EquivalentGroups.Add(group);
        context.SaveChanges();
        context.EquivalentGroupMembers.AddRange(
            new EquivalentGroupMember { GroupId = group.Id, PartId = original.Id, PartNo = PartNo },
            new EquivalentGroupMember { GroupId = group.Id, PartId = equivalent.Id, PartNo = EquivalentPartNo });
        context.SaveChanges();

        if (equivalentStock > 0)
        {
            var mainWh = context.Locations.First(l => l.Code == "DHL-BKK");
            context.PartStocks.Add(new PartStock { PartId = equivalent.Id, LocationId = mainWh.Id, GoodQty = equivalentStock, BadQty = 0 });
            context.SaveChanges();
        }
    }

    /// <summary>Syncs a ticket, submits+receives a withdraw batch so it's ready for a return test.
    /// SubmitWithdraw lands the batch on "รอ" (as long as the fixture's default 100-unit stock
    /// covers qty — see TicketController.TryAutoApprove); Admin still has to approve it manually
    /// (ApproveBatch → "รอส่งเมล DHL"), then confirm the DHL email went out
    /// (SendEmailConfirmedBatch → "เดินทาง") before ReceiveBatch has anything to receive.</summary>
    public static (Ticket Ticket, WithdrawBatch Batch) CreateReceivedTicket(TicketController controller, AppDbContext context, string externalNo, int qty = 1)
    {
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = externalNo, TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == externalNo);

        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = PartNo, Quantity = qty } },
            Address = "Withdraw Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        controller.ApproveBatch(ticket.TicketId, batch.WithdrawBatchId);
        controller.SendEmailConfirmedBatch(ticket.TicketId, batch.WithdrawBatchId);
        controller.ReceiveBatch(ticket.TicketId, batch.WithdrawBatchId);

        return (context.Tickets.First(t => t.TicketId == ticket.TicketId), context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId));
    }

    private class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = System.IO.Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
    }
}
