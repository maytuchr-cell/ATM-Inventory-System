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
/// location "OL_TECHNICIAN", main warehouse "WH-RAT" — see TicketController.ReceiveTicket /
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

        context.Parts.Add(new Part { PartNo = PartNo, PartName = "Test Part", IsActive = true });
        context.Locations.Add(new Location { Code = "OL-TECH", Name = "Technician Stock", LocationType = "OL_TECHNICIAN", IsActive = true });
        context.Locations.Add(new Location { Code = "WH-RAT", Name = "Ratchaburana Warehouse", LocationType = "RATCHABURANA", IsActive = true });
        context.SaveChanges();

        var stock = new StockService(context);
        var audit = new AuditService(context);
        var config = new ConfigurationBuilder().Build();
        var env = new FakeWebHostEnvironment();

        var controller = new TicketController(context, stock, audit, config, env);
        return (controller, context);
    }

    /// <summary>Syncs a ticket, submits+approves+receives a withdraw so it's ready for a return test.</summary>
    public static Ticket CreateReceivedTicket(TicketController controller, AppDbContext context, string externalNo, int qty = 1)
    {
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = externalNo, TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == externalNo);

        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = PartNo, Quantity = qty } },
            Address = "Withdraw Addr"
        });
        controller.ApproveTicket(ticket.TicketId);
        controller.ReceiveTicket(ticket.TicketId);

        return context.Tickets.First(t => t.TicketId == ticket.TicketId);
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
