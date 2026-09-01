using Api.Controllers;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Covers the เบิก/คืน (withdraw/return) state machine end to end. The withdraw leg lives on
/// WithdrawBatch (a Ticket can carry several independent ใบเบิก); the return leg stays on Ticket
/// itself (one active return cycle at a time, sourced from whichever batches are at เบิก). See
/// TicketController.cs for the state machine this mirrors.
/// </summary>
public class TicketWorkflowTests
{
    // ── Happy path — withdraw batch ─────────────────────────────────────────

    [Fact]
    public void SubmitWithdraw_WithSufficientStock_AutoApprovesAndDeductsStockImmediately()
    {
        // Auto-approve replaces the old manual-Approve-required flow: as long as the central
        // warehouse (seeded with 100 by the fixture) covers what's requested, submitting a
        // withdraw goes straight to รอส่งเมล DHL — stock cut right here — with no Admin click in
        // between. See TicketController.TryAutoApprove.
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1");

        var result = controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } },
            Address = "123 Main St"
        });

        Assert.IsType<OkObjectResult>(result);
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        Assert.Equal("รอส่งเมล DHL", batch.Status);
        Assert.Equal("Auto", batch.ApproverName);
        Assert.NotNull(batch.ApprovedAt);
        Assert.Null(batch.EmailSentAt); // not "เดินทาง" yet — that's SendEmailConfirmedBatch's job
        Assert.Equal("123 Main St", batch.WithdrawAddress);
        Assert.Single(context.TicketPartLines.Where(l => l.WithdrawBatchId == batch.WithdrawBatchId));

        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        Assert.Equal(98, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // seeded 100 - 2
    }

    [Fact]
    public void SendEmailConfirmedBatch_OnBatchWaitingToEmailDhl_MovesToTransit()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1C", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1C");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        Assert.Equal("รอส่งเมล DHL", batch.Status);

        var result = controller.SendEmailConfirmedBatch(ticket.TicketId, batch.WithdrawBatchId);

        Assert.IsType<OkObjectResult>(result);
        var updated = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId);
        Assert.Equal("เดินทาง", updated.Status);
        Assert.NotNull(updated.EmailSentAt);
    }

    [Fact]
    public void CancelBatch_OnceEmailedToDhl_IsLocked()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1D", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1D");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        controller.SendEmailConfirmedBatch(ticket.TicketId, batch.WithdrawBatchId);

        var result = controller.CancelBatch(ticket.TicketId, batch.WithdrawBatchId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("เดินทาง", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).Status);
    }

    [Fact]
    public void CancelBatch_WhileWaitingToEmailDhl_RestocksWarehouse()
    {
        // Not locked yet — Admin hasn't actually told DHL anything, just an internal
        // auto-approve commitment. Cancelling here should still hand the stock back.
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1E", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1E");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 4 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        Assert.Equal(96, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty);

        var result = controller.CancelBatch(ticket.TicketId, batch.WithdrawBatchId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Cancel", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).Status);
        Assert.Equal(100, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // back to seeded 100
    }

    [Fact]
    public void SubmitWithdraw_WithInsufficientStock_SetsStatusToWaitingForParts_AndLeavesStockUntouched()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        var stock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        stock.GoodQty = 1;
        context.SaveChanges();

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1B", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1B");

        var result = controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } },
            Address = "123 Main St"
        });

        Assert.IsType<OkObjectResult>(result);
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        Assert.Equal("รออะไหล่", batch.Status);
        Assert.Null(batch.ApprovedAt);
        Assert.NotNull(batch.WaitingSinceAt); // starts the 24h auto-reject-on-timeout clock
        Assert.Equal(1, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // untouched
    }

    [Fact]
    public void GetAllTickets_RejectsWaitingForPartsBatch_AfterTimeout_WithShortageInReason()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty = 1;
        context.SaveChanges();

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1C", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1C");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } },
            Address = "123 Main St"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        Assert.Equal("รออะไหล่", batch.Status);

        // Backdate past the 24h window, as if this had been sitting untouched since yesterday.
        batch.WaitingSinceAt = DateTime.Now.AddDays(-2);
        context.SaveChanges();

        controller.GetAllTickets(); // lazy timeout check runs here

        var updated = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId);
        Assert.Equal("Reject", updated.Status);
        Assert.Contains("Test Part", updated.RejectReason);
        Assert.Contains("ขาด 1", updated.RejectReason);
        Assert.Null(updated.WaitingSinceAt);
    }

    [Fact]
    public void GetAllTickets_LeavesWaitingForPartsBatch_UntilTimeoutElapses()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty = 1;
        context.SaveChanges();

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T1D", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T1D");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } },
            Address = "123 Main St"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);

        controller.GetAllTickets(); // well within the 24h window — must not reject yet

        var updated = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId);
        Assert.Equal("รออะไหล่", updated.Status);
    }

    [Fact]
    public void ApproveBatch_ThenReceive_MovesThroughเดินทางToเบิก_AndIssuesStock()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T2", qty: 3);

        Assert.Equal("เบิก", batch.Status);

        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var stock = context.PartStocks.First(s => s.LocationId == techLoc.Id);
        Assert.Equal(3, stock.GoodQty);
    }

    // ── Return leg (Ticket-scoped, unchanged from before Level B) ──────────

    [Fact]
    public void FullReturnFlow_RequiresAdminApprovalBeforeShip_ThenConfirmMovesStockToWarehouse()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T3", qty: 1);

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

        // Tech still can't ship until Admin confirms the DHL pickup email actually went out.
        var shipBeforeEmail = controller.MarkShipped(ticket.TicketId);
        Assert.IsType<BadRequestObjectResult>(shipBeforeEmail);

        controller.SendEmailConfirmedReturn(ticket.TicketId);
        Assert.Equal("กำลังเดินทางรับคืน", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);

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
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T4", qty: 1);

        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Lost" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.SendEmailConfirmedReturn(ticket.TicketId);
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
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T5", qty: 3);

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
        controller.SendEmailConfirmedReturn(ticket.TicketId);
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
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T6", qty: 1);

        var result = controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Broken" } },
            Address = "Return Addr"
        });

        Assert.IsType<BadRequestObjectResult>(result);
        // No return has ever started on this Ticket — its (return-only) Status stays null; the
        // batch itself is untouched at เบิก.
        Assert.Null(context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
        Assert.Equal("เบิก", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).Status);
    }

    [Fact]
    public void SubmitReturn_BeforeAnyBatchIsReceived_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T7", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T7");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        // Batch auto-approves (default stock) but is not yet emailed/received — no batch at เบิก.

        var result = controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void ApproveBatch_ManualRecheckOnStillShortBatch_ReturnsOk_StaysWaitingForParts_StockUntouched()
    {
        // Insufficient stock is now caught automatically at SubmitWithdraw (see TryAutoApprove),
        // not at a later manual Approve click — a "รออะไหล่" batch's Approve button is now a
        // manual recheck (for when stock might have recovered without a substitution), and it
        // no longer errors when stock is still short; it just reports nothing changed.
        var (controller, context) = TicketControllerFixture.Create();
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
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        Assert.Equal("รออะไหล่", batch.Status);

        var result = controller.ApproveBatch(ticket.TicketId, batch.WithdrawBatchId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("รออะไหล่", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).Status);
        Assert.Equal(2, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // untouched
    }

    [Fact]
    public void SubmitWithdraw_DeductsFromWarehouseImmediately_ViaAutoApprove_BeforeReceive()
    {
        // Stock now leaves WH-RAT the moment auto-approve runs (SubmitWithdraw) — not at Receive
        // and not at a later manual approve click.
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T14", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T14");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 4 } },
            Address = "Addr"
        });

        var mainWh  = context.Locations.First(l => l.Code == "WH-RAT");
        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        Assert.Equal(96, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // seeded 100 - 4
        Assert.Null(context.PartStocks.FirstOrDefault(s => s.LocationId == techLoc.Id)); // not yet at the tech
        Assert.Equal("รอส่งเมล DHL", context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId).Status);
    }

    [Fact]
    public void ReceiveBatch_OnlyAddsToTechLocation_WarehouseAlreadyDeductedAtApprove()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T15", qty: 4);
        Assert.Equal("เบิก", batch.Status);

        var mainWh  = context.Locations.First(l => l.Code == "WH-RAT");
        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var whStock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        var techStock = context.PartStocks.First(s => s.LocationId == techLoc.Id);

        Assert.Equal(96, whStock.GoodQty); // seeded 100 - 4, unchanged since ApproveBatch
        Assert.Equal(4, techStock.GoodQty);
    }

    [Fact]
    public void CancelBatch_WhileInTransit_RestocksWarehouse()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T16", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T16");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 4 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        controller.ApproveBatch(ticket.TicketId, batch.WithdrawBatchId);
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        Assert.Equal(96, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty);

        var result = controller.CancelBatch(ticket.TicketId, batch.WithdrawBatchId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Cancel", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).Status);
        Assert.Equal(100, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // back to seeded 100
    }

    [Fact]
    public void CancelBatch_WhileWaiting_DoesNotTouchStock()
    {
        // Stock is only ever deducted starting at Approve — cancelling before that (status รอ)
        // has nothing to undo.
        var (controller, context) = TicketControllerFixture.Create();
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty = 0; // force รอ, not auto-approved
        context.SaveChanges();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T17", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T17");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 4 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);

        controller.CancelBatch(ticket.TicketId, batch.WithdrawBatchId);

        Assert.Equal(0, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // untouched
    }

    [Fact]
    public void ConfirmReturnArrived_DeductsFromTechLocation_RegardlessOfCondition()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T15b", qty: 3);

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
        controller.SendEmailConfirmedReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);
        controller.ConfirmReturnArrived(ticket.TicketId);

        // All 3 units left the tech's hands regardless of what condition they came back in
        // (Good/Bad go to the warehouse, Lost is a write-off) — none of that changes that the
        // tech no longer has them.
        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var techStock = context.PartStocks.First(s => s.LocationId == techLoc.Id);
        Assert.Equal(0, techStock.GoodQty);
    }

    [Fact]
    public void ApproveReturn_WhenNotAwaitingApproval_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T8", qty: 1);
        // Ticket has a received batch, no return submitted yet — nothing to approve.

        var result = controller.ApproveReturn(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void MarkShipped_CalledTwice_SecondCallIsRejected()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T9", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.SendEmailConfirmedReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);

        var secondShip = controller.MarkShipped(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(secondShip);
    }

    [Fact]
    public void RejectBatch_ThenResubmit_ClearsOldLinesAndRejectReason()
    {
        // Reject only makes sense on a batch auto-approve couldn't already whisk away — seed
        // zero stock so both submits land (and stay) at รออะไหล่, keeping this test's focus on
        // reject/resubmit's line/reason cleanup rather than the auto-approve outcome.
        var (controller, context) = TicketControllerFixture.Create();
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty = 0;
        context.SaveChanges();

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T10", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T10");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        Assert.Equal("รออะไหล่", batch.Status);
        controller.RejectBatch(ticket.TicketId, batch.WithdrawBatchId, new RejectDto { Reason = "Wrong part" });
        Assert.Equal("Reject", context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).Status);
        var oldLineId = context.TicketPartLines.First(l => l.WithdrawBatchId == batch.WithdrawBatchId).TicketPartLineId;

        controller.ResubmitWithdrawBatch(ticket.TicketId, batch.WithdrawBatchId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 5 } },
            Address = "New Addr"
        });

        var updated = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId);
        Assert.Equal("รออะไหล่", updated.Status);
        Assert.Null(updated.RejectReason);
        var remainingLines = context.TicketPartLines.Where(l => l.WithdrawBatchId == batch.WithdrawBatchId).ToList();
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
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "T12", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.SendEmailConfirmedReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);
        controller.ConfirmReturnArrived(ticket.TicketId);

        var result = controller.CancelTicket(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── Multiple ใบเบิก under one Ticket ─────────────────────────────────────

    [Fact]
    public void SecondSubmitWithdraw_OnSameTicket_CreatesIndependentSecondBatch_NoSiblingTicketRow()
    {
        // The old "CreateAdditionalWithdraw" mechanic (a second Ticket row sharing the same
        // ExternalTicketNo) is gone — a second withdraw request is just another SubmitWithdraw
        // call against the same Ticket, creating a second WithdrawBatch instead.
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "ASV-100", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "ASV-100");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr 1"
        });

        var result = controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr 2"
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(context.Tickets.Where(t => t.ExternalTicketNo == "ASV-100")); // still one Ticket row
        Assert.Equal(2, context.WithdrawBatches.Count(b => b.TicketId == ticket.TicketId));
    }

    [Fact]
    public void SecondWithdrawBatch_SubmittedAndAutoApproved_DoesNotAffectFirstBatchsStatus()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, firstBatch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "ASV-200", qty: 1);
        Assert.Equal("เบิก", firstBatch.Status);

        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } },
            Address = "Addr 2"
        });

        // First batch is already at เบิก (fully received) — submitting the second batch (which
        // auto-approves on its own, stock permitting) must not touch it.
        var firstAfter = context.WithdrawBatches.First(b => b.WithdrawBatchId == firstBatch.WithdrawBatchId);
        Assert.Equal("เบิก", firstAfter.Status);
        var secondBatch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId && b.WithdrawBatchId != firstBatch.WithdrawBatchId);
        Assert.Equal("รอส่งเมล DHL", secondBatch.Status);
    }

    [Fact]
    public void SubmitReturn_SourcesLinesFromWhicheverBatchesAreเบิก_NotJustOne()
    {
        // With two received batches under one Ticket, a return can be submitted against parts
        // from either (or both) — this is what replaces the old client-side "combine sibling
        // Tickets into one return" hack.
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, firstBatch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "ASV-300", qty: 1);
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr 2"
        });
        var secondBatch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId && b.WithdrawBatchId != firstBatch.WithdrawBatchId);
        controller.SendEmailConfirmedBatch(ticket.TicketId, secondBatch.WithdrawBatchId);
        controller.ReceiveBatch(ticket.TicketId, secondBatch.WithdrawBatchId);

        var result = controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2, Condition = "Good" } },
            Address = "Return Addr"
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("รอ", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
    }

    // ── Return leg: Reject/Cancel, DHL email confirmation, off-ticket lines ─────

    [Fact]
    public void RejectReturn_ClearsReturnLinesAndReasonRevertsToเบิก_ForResubmit()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "RT-REJ-1", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });

        var result = controller.RejectReturn(ticket.TicketId, new RejectDto { Reason = "Wrong condition noted" });

        Assert.IsType<OkObjectResult>(result);
        var updated = context.Tickets.First(t => t.TicketId == ticket.TicketId);
        Assert.Equal("เบิก", updated.Status); // reverted — tech still has the part, can resubmit
        Assert.Equal("Wrong condition noted", updated.RejectReason);
        Assert.Null(updated.ReturnAddress);
        Assert.Empty(context.TicketPartLines.Where(l => l.TicketId == ticket.TicketId && l.LineType == "Return"));
    }

    [Fact]
    public void SubmitReturn_AfterRejection_ClearsThePriorReason()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "RT-REJ-2", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.RejectReturn(ticket.TicketId, new RejectDto { Reason = "Fix this" });

        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr v2"
        });

        var updated = context.Tickets.First(t => t.TicketId == ticket.TicketId);
        Assert.Equal("รอ", updated.Status);
        Assert.Null(updated.RejectReason);
    }

    [Fact]
    public void SendEmailConfirmedReturn_MovesApprovedReturnToTransitToCollect()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "RT-EMAIL-1", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);

        var result = controller.SendEmailConfirmedReturn(ticket.TicketId);

        Assert.IsType<OkObjectResult>(result);
        var updated = context.Tickets.First(t => t.TicketId == ticket.TicketId);
        Assert.Equal("กำลังเดินทางรับคืน", updated.Status);
        Assert.NotNull(updated.ReturnEmailSentAt);
    }

    [Fact]
    public void CancelTicket_OnceReturnEmailedToDhl_IsLocked()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "RT-EMAIL-2", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.SendEmailConfirmedReturn(ticket.TicketId);

        var result = controller.CancelTicket(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("กำลังเดินทางรับคืน", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
    }

    [Fact]
    public void CancelTicket_OnceReturnShipped_IsStillLocked()
    {
        // Regression: เดินทาง on the return leg only happens after กำลังเดินทางรับคืน (the DHL-email
        // lock point), so it must stay locked — cancelling here must not be allowed to undo stock
        // that was never touched by return-side actions in the first place.
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "RT-EMAIL-4", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);
        controller.SendEmailConfirmedReturn(ticket.TicketId);
        controller.MarkShipped(ticket.TicketId);

        var result = controller.CancelTicket(ticket.TicketId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("เดินทาง", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
    }

    [Fact]
    public void CancelTicket_OnApprovedReturnBeforeEmail_IsStillAllowed()
    {
        // Not locked yet — Admin approved internally but hasn't told DHL anything.
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "RT-EMAIL-3", qty: 1);
        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" } },
            Address = "Return Addr"
        });
        controller.ApproveReturn(ticket.TicketId);

        var result = controller.CancelTicket(ticket.TicketId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Cancel", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
    }

    [Fact]
    public void GetAllTickets_FlagsReturnLineNotOnOriginalWithdrawAsOffTicket()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "RT-OFF-1", qty: 1);
        TicketControllerFixture.SeedEquivalentPart(context); // gives us a second real Part to use as the "extra" one

        controller.SubmitReturn(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new()
            {
                new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1, Condition = "Good" },              // matches the withdraw
                new LineDto { PartNo = TicketControllerFixture.EquivalentPartNo, Quantity = 1, Condition = "Good" },    // never withdrawn on this ticket
            },
            Address = "Return Addr"
        });

        var ok = Assert.IsType<OkObjectResult>(controller.GetAllTickets());
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var ticketJson = doc.RootElement.EnumerateArray().First(t => t.GetProperty("TicketId").GetInt32() == ticket.TicketId);
        var returnLines = ticketJson.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("LineType").GetString() == "Return").ToList();

        var matching = returnLines.First(l => l.GetProperty("PartNo").GetString() == TicketControllerFixture.PartNo);
        var offTicket = returnLines.First(l => l.GetProperty("PartNo").GetString() == TicketControllerFixture.EquivalentPartNo);
        Assert.False(matching.GetProperty("isOffTicket").GetBoolean());
        Assert.True(offTicket.GetProperty("isOffTicket").GetBoolean());
    }

    // ── Substitute — show the tech's original request after Admin swaps it ────

    [Fact]
    public void SubstitutePart_OnRegisteredEquivalent_SetsPartNoAndRecordsOriginalPartNo()
    {
        var (controller, context) = TicketControllerFixture.Create();
        // Substitution only has a window while the batch is waiting — with the fixture's default
        // 100-unit stock the submit below would auto-approve straight past "waiting" before this
        // test gets a chance to substitute anything, so zero it out first.
        context.PartStocks.First(s => s.LocationId == context.Locations.First(l => l.Code == "WH-RAT").Id).GoodQty = 0;
        context.SaveChanges();
        TicketControllerFixture.SeedEquivalentPart(context);
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "SUB-1", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "SUB-1");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        var line = context.TicketPartLines.First(l => l.WithdrawBatchId == batch.WithdrawBatchId);

        var result = controller.SubstitutePart(ticket.TicketId, batch.WithdrawBatchId, line.TicketPartLineId,
            new SubstituteDto { PartNo = TicketControllerFixture.EquivalentPartNo });

        Assert.IsType<OkObjectResult>(result);
        var lineAfter = context.TicketPartLines.First(l => l.TicketPartLineId == line.TicketPartLineId);
        Assert.Equal(TicketControllerFixture.EquivalentPartNo, lineAfter.PartNo);
        Assert.Equal(TicketControllerFixture.PartNo, lineAfter.OriginalPartNo);
    }

    [Fact]
    public void SubstitutePart_OnUnregisteredPart_ReturnsBadRequestAndLeavesLineUnchanged()
    {
        var (controller, context) = TicketControllerFixture.Create();
        context.PartStocks.First(s => s.LocationId == context.Locations.First(l => l.Code == "WH-RAT").Id).GoodQty = 0;
        // A second part exists but is never registered as an equivalent of PartNo.
        context.Parts.Add(new Part { PartNo = "TEST-PART-UNRELATED", PartName = "Unrelated", IsActive = true });
        context.SaveChanges();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "SUB-2", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "SUB-2");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        var line = context.TicketPartLines.First(l => l.WithdrawBatchId == batch.WithdrawBatchId);

        var result = controller.SubstitutePart(ticket.TicketId, batch.WithdrawBatchId, line.TicketPartLineId,
            new SubstituteDto { PartNo = "TEST-PART-UNRELATED" });

        Assert.IsType<BadRequestObjectResult>(result);
        var lineAfter = context.TicketPartLines.First(l => l.TicketPartLineId == line.TicketPartLineId);
        Assert.Equal(TicketControllerFixture.PartNo, lineAfter.PartNo);
        Assert.Null(lineAfter.OriginalPartNo);
    }

    [Fact]
    public void SubstitutePart_CalledTwice_KeepsTheTrueOriginalPartNo()
    {
        // Admin changes their mind and substitutes a second time — OriginalPartNo must still
        // point at what the tech actually requested, not the first substitute.
        var (controller, context) = TicketControllerFixture.Create();
        context.PartStocks.First(s => s.LocationId == context.Locations.First(l => l.Code == "WH-RAT").Id).GoodQty = 0;
        TicketControllerFixture.SeedEquivalentPart(context);
        var thirdPart = new Part { PartNo = "TEST-PART-003", PartName = "Third", IsActive = true };
        context.Parts.Add(thirdPart);
        context.SaveChanges();
        var groupId = context.EquivalentGroupMembers.First(m => m.PartNo == TicketControllerFixture.PartNo).GroupId;
        context.EquivalentGroupMembers.Add(new EquivalentGroupMember { GroupId = groupId, PartId = thirdPart.Id, PartNo = thirdPart.PartNo });
        context.SaveChanges();

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "SUB-3", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "SUB-3");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        var line = context.TicketPartLines.First(l => l.WithdrawBatchId == batch.WithdrawBatchId);

        controller.SubstitutePart(ticket.TicketId, batch.WithdrawBatchId, line.TicketPartLineId,
            new SubstituteDto { PartNo = TicketControllerFixture.EquivalentPartNo });
        controller.SubstitutePart(ticket.TicketId, batch.WithdrawBatchId, line.TicketPartLineId,
            new SubstituteDto { PartNo = thirdPart.PartNo });

        var lineAfter = context.TicketPartLines.First(l => l.TicketPartLineId == line.TicketPartLineId);
        Assert.Equal(thirdPart.PartNo, lineAfter.PartNo);
        Assert.Equal(TicketControllerFixture.PartNo, lineAfter.OriginalPartNo);
    }

    [Fact]
    public void SubstitutePart_WhenBatchNotWaiting_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        TicketControllerFixture.SeedEquivalentPart(context);
        var (ticket, batch) = TicketControllerFixture.CreateReceivedTicket(controller, context, "SUB-4", qty: 1);
        var line = context.TicketPartLines.First(l => l.WithdrawBatchId == batch.WithdrawBatchId);

        // Batch is already เบิก (received), not รอ — substitution window has closed.
        var result = controller.SubstitutePart(ticket.TicketId, batch.WithdrawBatchId, line.TicketPartLineId,
            new SubstituteDto { PartNo = TicketControllerFixture.EquivalentPartNo });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void GetAllTickets_OnSubstitutedLine_IncludesOriginalPartNoAndName()
    {
        var (controller, context) = TicketControllerFixture.Create();
        context.PartStocks.First(s => s.LocationId == context.Locations.First(l => l.Code == "WH-RAT").Id).GoodQty = 0;
        TicketControllerFixture.SeedEquivalentPart(context);
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "SUB-5", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "SUB-5");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        var line = context.TicketPartLines.First(l => l.WithdrawBatchId == batch.WithdrawBatchId);
        controller.SubstitutePart(ticket.TicketId, batch.WithdrawBatchId, line.TicketPartLineId,
            new SubstituteDto { PartNo = TicketControllerFixture.EquivalentPartNo });

        var ok = Assert.IsType<OkObjectResult>(controller.GetAllTickets());
        // The response is an anonymous type (internal to the Api assembly), so read it back
        // through JSON instead of dynamic — dynamic binding can't see internal anonymous types
        // across assemblies.
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var ticketJson = doc.RootElement.EnumerateArray()
            .First(t => t.GetProperty("TicketId").GetInt32() == ticket.TicketId);
        var batchJson = ticketJson.GetProperty("withdrawBatches").EnumerateArray().First();
        var lineJson = batchJson.GetProperty("lines").EnumerateArray().First();

        Assert.Equal(TicketControllerFixture.PartNo, lineJson.GetProperty("OriginalPartNo").GetString());
        Assert.Equal("Test Part", lineJson.GetProperty("originalPartName").GetString());
    }

    // ── Withdraw slip number / usage status ────────────────────────────────────

    [Fact]
    public void SubmitWithdraw_GeneratesWithdrawSlipNo_AndStoresFormFields()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "WD-T1", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "WD-T1");

        var result = controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr",
            WithdrawDate = new DateTime(2026, 3, 2),
            EmployeeCode = "EMP001",
            UsageStatus = "Repair"
        });

        Assert.IsType<OkObjectResult>(result);
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        Assert.Matches(@"^WD-\d{4}-\d{5}$", batch.WithdrawSlipNo);
        Assert.Equal(new DateTime(2026, 3, 2), batch.WithdrawDate);
        Assert.Equal("EMP001", batch.EmployeeCode);
        Assert.Equal("Repair", batch.UsageStatus);
    }

    [Fact]
    public void SubmitWithdraw_TwoTicketsSameYear_GetSequentialSlipNumbers()
    {
        var (controller, context) = TicketControllerFixture.Create();
        TicketControllerFixture.SeedEquivalentPart(context); // second part so both lines have stock

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "WD-T2A", TechName = "Tech" });
        var t1 = context.Tickets.First(t => t.ExternalTicketNo == "WD-T2A");
        controller.SubmitWithdraw(t1.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } }, Address = "Addr"
        });

        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "WD-T2B", TechName = "Tech" });
        var t2 = context.Tickets.First(t => t.ExternalTicketNo == "WD-T2B");
        controller.SubmitWithdraw(t2.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.EquivalentPartNo, Quantity = 1 } }, Address = "Addr"
        });

        var slip1 = context.WithdrawBatches.First(b => b.TicketId == t1.TicketId).WithdrawSlipNo;
        var slip2 = context.WithdrawBatches.First(b => b.TicketId == t2.TicketId).WithdrawSlipNo;
        Assert.NotEqual(slip1, slip2);
        var year = DateTime.Now.Year;
        Assert.Equal($"WD-{year}-00001", slip1);
        Assert.Equal($"WD-{year}-00002", slip2);
    }

    [Fact]
    public void RejectBatch_ThenResubmit_KeepsTheSameWithdrawSlipNo()
    {
        // Resubmitting after Reject is still the same ใบเบิก, not a new one.
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "WD-T3", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "WD-T3");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } }, Address = "Addr"
        });
        var batch = context.WithdrawBatches.First(b => b.TicketId == ticket.TicketId);
        var firstSlipNo = batch.WithdrawSlipNo;

        controller.RejectBatch(ticket.TicketId, batch.WithdrawBatchId, new RejectDto { Reason = "ผิดรุ่น" });
        controller.ResubmitWithdrawBatch(ticket.TicketId, batch.WithdrawBatchId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } }, Address = "Addr2"
        });

        var secondSlipNo = context.WithdrawBatches.First(b => b.WithdrawBatchId == batch.WithdrawBatchId).WithdrawSlipNo;
        Assert.Equal(firstSlipNo, secondSlipNo);
    }
}
