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
    public void ApproveTicket_WithInsufficientWarehouseStock_ReturnsBadRequest_AndLeavesStockUntouched()
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

        // ApproveTicket never called SaveChanges (it returned before reaching it), so nothing was
        // actually persisted — only the in-memory change tracker still holds the failed attempt.
        // A real request gets a fresh DbContext next time, so clear it here to check what the
        // database would actually show.
        context.ChangeTracker.Clear();
        Assert.Equal(2, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // untouched
    }

    [Fact]
    public void ApproveTicket_DeductsFromWarehouseImmediately_BeforeReceive()
    {
        // Stock now leaves WH-RAT the moment Admin approves — not at Receive — so a second
        // ticket can't be approved into stock this one already claimed while still in transit.
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T14", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T14");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 4 } },
            Address = "Addr"
        });

        controller.ApproveTicket(ticket.TicketId);

        var mainWh  = context.Locations.First(l => l.Code == "WH-RAT");
        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        Assert.Equal(96, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // seeded 100 - 4
        Assert.Null(context.PartStocks.FirstOrDefault(s => s.LocationId == techLoc.Id)); // not yet at the tech
        Assert.Equal("เดินทาง", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
    }

    [Fact]
    public void ReceiveTicket_OnlyAddsToTechLocation_WarehouseAlreadyDeductedAtApprove()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "T15", qty: 4);
        Assert.Equal("เบิก", ticket.Status);

        var mainWh  = context.Locations.First(l => l.Code == "WH-RAT");
        var techLoc = context.Locations.First(l => l.LocationType == "OL_TECHNICIAN");
        var whStock = context.PartStocks.First(s => s.LocationId == mainWh.Id);
        var techStock = context.PartStocks.First(s => s.LocationId == techLoc.Id);

        Assert.Equal(96, whStock.GoodQty); // seeded 100 - 4, unchanged since ApproveTicket
        Assert.Equal(4, techStock.GoodQty);
    }

    [Fact]
    public void CancelTicket_WhileInTransit_RestocksWarehouse()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T16", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T16");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 4 } },
            Address = "Addr"
        });
        controller.ApproveTicket(ticket.TicketId);
        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        Assert.Equal(96, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty);

        var result = controller.CancelTicket(ticket.TicketId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Cancel", context.Tickets.First(t => t.TicketId == ticket.TicketId).Status);
        Assert.Equal(100, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // back to seeded 100
    }

    [Fact]
    public void CancelTicket_WhileWaiting_DoesNotTouchStock()
    {
        // Stock is only ever deducted starting at Approve — cancelling before that (status รอ)
        // has nothing to undo.
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "T17", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "T17");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 4 } },
            Address = "Addr"
        });

        controller.CancelTicket(ticket.TicketId);

        var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
        Assert.Equal(100, context.PartStocks.First(s => s.LocationId == mainWh.Id).GoodQty); // untouched
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

    // ── Multiple ใบเบิก under one Aservice Ticket ───────────────────────────

    [Fact]
    public void CreateAdditionalWithdraw_OnKnownExternalTicketNo_CreatesIndependentSecondTicket()
    {
        var (controller, context) = TicketControllerFixture.Create();
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "ASV-100", TechName = "Tech" });
        var first = context.Tickets.First(t => t.ExternalTicketNo == "ASV-100");

        var result = controller.CreateAdditionalWithdraw(new SyncTicketDto
        {
            ExternalTicketNo = "ASV-100", TechName = "Tech", TechDept = "Zone A"
        });

        Assert.IsType<OkObjectResult>(result);
        var rows = context.Tickets.Where(t => t.ExternalTicketNo == "ASV-100").ToList();
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(first.TicketId, rows.First(t => t.TicketId != first.TicketId).TicketId);
        Assert.All(rows, t => Assert.Null(t.Status)); // both start fresh, independently withdrawable
    }

    [Fact]
    public void CreateAdditionalWithdraw_OnUnknownExternalTicketNo_ReturnsBadRequest()
    {
        var (controller, _) = TicketControllerFixture.Create();

        var result = controller.CreateAdditionalWithdraw(new SyncTicketDto
        {
            ExternalTicketNo = "NEVER-SYNCED", TechName = "Tech"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void SecondWithdrawSlip_ApprovedAndReceived_DoesNotAffectFirstSlipsStatus()
    {
        var (controller, context) = TicketControllerFixture.Create();
        var first = TicketControllerFixture.CreateReceivedTicket(controller, context, "ASV-200", qty: 1);
        Assert.Equal("เบิก", first.Status);

        controller.CreateAdditionalWithdraw(new SyncTicketDto { ExternalTicketNo = "ASV-200", TechName = "Tech" });
        var second = context.Tickets.First(t => t.ExternalTicketNo == "ASV-200" && t.TicketId != first.TicketId);
        controller.SubmitWithdraw(second.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 2 } },
            Address = "Addr 2"
        });

        // First slip is already at เบิก (fully received) — submitting/approving the second slip
        // must not touch it.
        var firstAfter = context.Tickets.First(t => t.TicketId == first.TicketId);
        Assert.Equal("เบิก", firstAfter.Status);
        var secondAfter = context.Tickets.First(t => t.TicketId == second.TicketId);
        Assert.Equal("รอ", secondAfter.Status);
    }

    // ── Substitute — show the tech's original request after Admin swaps it ────

    [Fact]
    public void SubstitutePart_OnRegisteredEquivalent_SetsPartNoAndRecordsOriginalPartNo()
    {
        var (controller, context) = TicketControllerFixture.Create();
        TicketControllerFixture.SeedEquivalentPart(context);
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "SUB-1", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "SUB-1");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var line = context.TicketPartLines.First(l => l.TicketId == ticket.TicketId);

        var result = controller.SubstitutePart(ticket.TicketId, line.TicketPartLineId,
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
        var line = context.TicketPartLines.First(l => l.TicketId == ticket.TicketId);

        var result = controller.SubstitutePart(ticket.TicketId, line.TicketPartLineId,
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
        var line = context.TicketPartLines.First(l => l.TicketId == ticket.TicketId);

        controller.SubstitutePart(ticket.TicketId, line.TicketPartLineId,
            new SubstituteDto { PartNo = TicketControllerFixture.EquivalentPartNo });
        controller.SubstitutePart(ticket.TicketId, line.TicketPartLineId,
            new SubstituteDto { PartNo = thirdPart.PartNo });

        var lineAfter = context.TicketPartLines.First(l => l.TicketPartLineId == line.TicketPartLineId);
        Assert.Equal(thirdPart.PartNo, lineAfter.PartNo);
        Assert.Equal(TicketControllerFixture.PartNo, lineAfter.OriginalPartNo);
    }

    [Fact]
    public void SubstitutePart_WhenTicketNotWaiting_ReturnsBadRequest()
    {
        var (controller, context) = TicketControllerFixture.Create();
        TicketControllerFixture.SeedEquivalentPart(context);
        var ticket = TicketControllerFixture.CreateReceivedTicket(controller, context, "SUB-4", qty: 1);
        var line = context.TicketPartLines.First(l => l.TicketId == ticket.TicketId);

        // Ticket is already เบิก (received), not รอ — substitution window has closed.
        var result = controller.SubstitutePart(ticket.TicketId, line.TicketPartLineId,
            new SubstituteDto { PartNo = TicketControllerFixture.EquivalentPartNo });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void GetAllTickets_OnSubstitutedLine_IncludesOriginalPartNoAndName()
    {
        var (controller, context) = TicketControllerFixture.Create();
        TicketControllerFixture.SeedEquivalentPart(context);
        controller.SyncFromAservice(new SyncTicketDto { ExternalTicketNo = "SUB-5", TechName = "Tech" });
        var ticket = context.Tickets.First(t => t.ExternalTicketNo == "SUB-5");
        controller.SubmitWithdraw(ticket.TicketId, new SubmitLinesDto
        {
            Lines = new() { new LineDto { PartNo = TicketControllerFixture.PartNo, Quantity = 1 } },
            Address = "Addr"
        });
        var line = context.TicketPartLines.First(l => l.TicketId == ticket.TicketId);
        controller.SubstitutePart(ticket.TicketId, line.TicketPartLineId,
            new SubstituteDto { PartNo = TicketControllerFixture.EquivalentPartNo });

        var ok = Assert.IsType<OkObjectResult>(controller.GetAllTickets());
        // The response is an anonymous type (internal to the Api assembly), so read it back
        // through JSON instead of dynamic — dynamic binding can't see internal anonymous types
        // across assemblies.
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var ticketJson = doc.RootElement.EnumerateArray()
            .First(t => t.GetProperty("TicketId").GetInt32() == ticket.TicketId);
        var lineJson = ticketJson.GetProperty("lines").EnumerateArray().First();

        Assert.Equal(TicketControllerFixture.PartNo, lineJson.GetProperty("OriginalPartNo").GetString());
        Assert.Equal("Test Part", lineJson.GetProperty("originalPartName").GetString());
    }
}
