using Api.Controllers;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Covers the เบิก/คืน (withdraw/return) Ticket state machine end to end: sync, submit,
/// approve, receive, submit-return, the Admin approve-return gate, ship, and confirm-return
/// (including the resulting stock adjustment). See TicketController.cs for the state machine
/// this mirrors.
/// </summary>
public class TicketWorkflowTests
{
    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void SubmitWithdraw_OnNewTicket_SetsStatusToWaiting()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1");

        var result = controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } },
            Address = "123 Main St"
        });

        Assert.IsType<OkObjectResult>(result);
        var updated = context.Tickets.First(t => t.TicketId == ticket.TicketId);
        Assert.Equal("รอ", updated.Status);
        Assert.Equal("123 Main St", updated.WithdrawAddress);
        Assert.Single(context.TicketPartLines.Where(l => l.TicketId == ticket.TicketId));
    }

    [Fact]
    public void ApproveTicket_ThenReceive_MovesThroughเดินทางToเบิก_AndIssuesStock()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T2", qty: 3);

        Assert.Equal("เบิก", ticket.Status);

        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var stock = context.PartStocks.First(s => s.LocationId == techLoc.Id);
        Assert.Equal(3, stock.GoodQty);
    }

    [Fact]
    public void FullReturnFlow_RequiresAdminApprovalBeforeShip_ThenConfirmMovesStockToWarehouse()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T3", qty: 1);

        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        Assert.Equal("รอ", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);

        // Tech cannot ship before Admin approves the return.
        var shipBeforeApproval = controller.MarkShipped(ticket.TicketId);
        Assert.IsType<BadRequestObjectResult>(shipBeforeApproval);

        controller.ApproveReturn(ticket.TicketId);
        Assert.Equal("อนุมัติคืน", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);

        var shipAfterApproval = controller.MarkShipped(ticket.TicketId);
        Assert.IsType<OkObjectResult>(shipAfterApproval);
        Assert.Equal("เดินทาง", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);

        controller.ConfirmReturnArrived(ticket.TicketId);
        var final = context.Tickets.First(t => t.TicketId == ticket.TicketId);
        Assert.Equal("คืน", final.Status);

        // Fixture seeds 100 at WH-RAT; withdrawing 1 then returning 1 Good nets back to 100.
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        Assert.Equal(100, stock.GoodQty);
    }

    [Fact]
    public void ConfirmReturnArrived_WithLostCondition_DoesNotAddStock()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T4", qty: 1);

        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Lost" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);
        controller.ConfirmReturnArrived(ticket.TicketId);

        // Withdrawing 1 (100 -> 99) and a Lost return adds nothing back — stays at 99.
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        Assert.Equal(99, stock.GoodQty);
        Assert.Equal("คืน", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
    }

    [Fact]
    public void ConfirmReturnArrived_WithMixedConditions_SplitsIntoGoodAndBadBuckets()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T5", qty: 3);

        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new()
            {
                new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" },
                new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Bad" },
                new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Lost" },
            },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);
        controller.ConfirmReturnArrived(ticket.TicketId);

        // Withdrew 3 (100 -> 97); returned 1 Good (97 -> 98), 1 Bad, 1 Lost (no change).
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        Assert.Equal(98, stock.GoodQty);
        Assert.Equal(1, stock.BadQty);
    }

    // ── Validation / guard rails ────────────────────────────────────────────

    [Fact]
    public void SubmitReturn_WithInvalidCondition_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T6", qty: 1);

        var result = controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Broken" } },
            Address = "Return Addr"
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("เบิก", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status); // status unchanged
    }

    [Fact]
    public void SubmitReturn_BeforeWithdrawIsReceived_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T7", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T7");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        // Not yet approved/received — still "รอ" on the withdraw leg.

        var result = controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void ApproveTicket_WithInsufficientWarehouseStock_ReturnsBadRequest_AndReceiveActuallyMovesStock()
    {
        var (controller, context) = TicketControllerFixture.Create();
        // Fixture seeds 100 GoodQty at WH-RAT by default — drop it below what's requested.
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        stock.GoodQty = 2;
        context.SaveChanges();

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T13", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T13");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 5 } },
            Address = "Addr"
        });

        var result = controller.ApproveTicket(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("รอ", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
        Assert.Equal(2, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // untouched
    }

    [Fact]
    public void ReceiveTicket_DeductsFromWarehouse_AndAddsToTechLocation()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T14", qty: 4);
        Assert.Equal("เบิก", ticket.Status);

        var mainWh  = context.Locations.First(l => l.Code == "WH-RAT");
        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var whStock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        var techStock = context.PartStocks.First(s => s.LocationId == techLoc.Id);

        Assert.Equal(96, whStock.GoodQty); // seeded 100 - 4
        Assert.Equal(4, techStock.GoodQty);
    }

    [Fact]
    public void ConfirmReturnArrived_DeductsFromTechLocation_RegardlessOfCondition()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T15", qty: 3);

        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new()
            {
                new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" },
                new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Bad" },
                new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Lost" },
            },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);
        controller.ConfirmReturnArrived(ticket.TicketId);

        // All 3 units left the tech's hands regardless of what condition they came back in
        // (Good/Bad go to the warehouse, Lost is a write-off) — none of that changes that the
        // tech no longer has them. Previously ConfirmReturnArrived never touched techLoc at all,
        // so this bucket only ever grew.
        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var techStock = context.PartStocks.First(s => s.LocationId == techLoc.Id);
        Assert.Equal(0, techStock.GoodQty);
    }

    [Fact]
    public void ApproveReturn_WhenNotAwaitingApproval_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T8", qty: 1);
        // Ticket is at เบิก, no return submitted yet — nothing to approve.

        var result = controller.ApproveReturn(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void MarkShipped_CalledTwice_SecondCallIsRejected()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T9", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);

        var secondShip = controller.MarkShipped(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(secondShip);
    }

    [Fact]
    public void RejectTicket_ThenResubmitWithdraw_ClearsOldLinesAndRejectReason()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T10", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T10");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        controller.RejectTicket(ticket.TicketId, new RejectDto { Reason = "Wrong part" });
        Assert.Equal("Reject", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
        var oldLineId = context.TicketPartLines.First(l => l.TicketId == ticket.TicketId).TicketPartLineId;

        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 5 } },
            Address = "New Addr"
        });

        var updated = context.Tickets.First(t => t.TicketId == ticket.TicketId);
        Assert.Equal("รอ", updated.Status);
        Assert.Null(updated.RejectReason);
        var remainingLines = context.TicketPartLines.Where(l => l.TicketId == ticket.TicketId).ToList();
        Assert.Single(remainingLines);
        Assert.NotEqual(oldLineId, remainingLines[0].TicketPartLineId);
        Assert.Equal(5, remainingLines[0].Quantity);
    }

    [Fact]
    public void SubmitWithdraw_WithInactivePart_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        context.Parts.Add(new Part { PartNo = "INACTIVE-001", PartName = "Retired part", IsActive = false });
        context.SaveChanges();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T11", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T11");

        var result = controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = "INACTIVE-001", Quantity = 1 } },
            Address = "Addr"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void CancelTicket_OnAlreadyReturnedTicket_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T12", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);
        controller.ConfirmReturnArrived(ticket.TicketId);

        var result = controller.CancelTicket(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
