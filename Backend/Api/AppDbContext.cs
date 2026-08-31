using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace Api.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // SQLite allows only one writer at a time — under concurrent usage (many techs saving within
    // the same instant) a second writer can hit SQLITE_BUSY/SQLITE_LOCKED even within the
    // busy_timeout window in rare races. Retrying a few times with a short backoff absorbs that
    // instead of surfacing a raw "database is locked" error to the user for what's really just a
    // few-millisecond queueing delay. Not a fix for sustained write contention — if this starts
    // firing often, that's a signal to move off SQLite, not a reason to add more retries.
    private static readonly int[] RetryDelaysMs = { 50, 150, 400 };

    private static bool IsTransientSqliteLock(Exception ex) =>
        ex is SqliteException { SqliteErrorCode: 5 or 6 } // 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED
        || ex.InnerException is SqliteException { SqliteErrorCode: 5 or 6 };


    public DbSet<Part> Parts { get; set; }
    public DbSet<PartImage> PartImages { get; set; }
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<TicketPartLine> TicketPartLines { get; set; }
    public DbSet<TicketAttachment> TicketAttachments { get; set; }
    public DbSet<WithdrawBatch> WithdrawBatches { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<Vendor> Vendors { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<PartStock> PartStocks { get; set; }
    public DbSet<PartUnit> PartUnits { get; set; }
    public DbSet<DailyReportImportBatch> DailyReportImportBatches { get; set; }
    public DbSet<DailyReportImportRow> DailyReportImportRows { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<GoodsReceipt> GoodsReceipts { get; set; }
    public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; }
    public DbSet<ReturnRequest> ReturnRequests { get; set; }
    public DbSet<EquivalentPart> EquivalentParts { get; set; }
    public DbSet<StockTransfer> StockTransfers { get; set; }
    public DbSet<StockCount> StockCounts { get; set; }
    public DbSet<StockCountLine> StockCountLines { get; set; }
    public DbSet<SystemSettings> SystemSettings { get; set; }
    public DbSet<DisposalRequest> DisposalRequests { get; set; }
    public DbSet<EquivalentGroup> EquivalentGroups { get; set; }
    public DbSet<EquivalentGroupMember> EquivalentGroupMembers { get; set; }
    public DbSet<AtmModel> AtmModels { get; set; }
    public DbSet<AtmModelPart> AtmModelParts { get; set; }
    public DbSet<User> Users { get; set; }

    // Bump concurrency tokens on every insert/update so the original value is used in the
    // UPDATE ... WHERE RowVersion = @original check. A mismatch throws DbUpdateConcurrencyException.
    private void BumpConcurrencyTokens()
    {
        foreach (var e in ChangeTracker.Entries())
        {
            if (e.State != EntityState.Added && e.State != EntityState.Modified) continue;
            if (e.Entity is Part p) p.RowVersion = Guid.NewGuid().ToString("N");
            else if (e.Entity is PartStock s) s.RowVersion = Guid.NewGuid().ToString("N");
        }
    }

    public override int SaveChanges()
    {
        BumpConcurrencyTokens();
        for (var attempt = 0; ; attempt++)
        {
            try { return base.SaveChanges(); }
            catch (Exception ex) when (attempt < RetryDelaysMs.Length && IsTransientSqliteLock(ex))
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpConcurrencyTokens();
        for (var attempt = 0; ; attempt++)
        {
            try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
            catch (Exception ex) when (attempt < RetryDelaysMs.Length && IsTransientSqliteLock(ex))
            {
                await Task.Delay(RetryDelaysMs[attempt], cancellationToken);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Part.PartNo unique index
        modelBuilder.Entity<Part>()
            .HasIndex(p => p.PartNo)
            .IsUnique();

        // Optimistic-concurrency tokens (portable across SQLite/MySQL — bumped in SaveChanges)
        modelBuilder.Entity<Part>().Property(p => p.RowVersion).IsConcurrencyToken();
        modelBuilder.Entity<PartStock>().Property(s => s.RowVersion).IsConcurrencyToken();

        // Part → Category (nullable FK, SetNull on delete)
        modelBuilder.Entity<Part>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Parts)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // One Ticket row per Case No. — multiple independent ใบเบิก under the same case now live
        // as WithdrawBatch rows (see WithdrawBatch.cs) instead of separate sibling Ticket rows.
        modelBuilder.Entity<Ticket>()
            .HasIndex(t => t.ExternalTicketNo)
            .IsUnique();

        modelBuilder.Entity<TicketPartLine>()
            .HasOne(l => l.Ticket)
            .WithMany(t => t.Lines)
            .HasForeignKey(l => l.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TicketPartLine>()
            .HasOne(l => l.Part)
            .WithMany()
            .HasForeignKey(l => l.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        // WithdrawBatch ("ใบเบิก") — see WithdrawBatch.cs. A Ticket's withdraw leg is being split
        // out to allow multiple independent ใบเบิก per Case No.; the return leg stays Ticket-scoped.
        modelBuilder.Entity<WithdrawBatch>()
            .HasOne(b => b.Ticket)
            .WithMany(t => t.WithdrawBatches)
            .HasForeignKey(b => b.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TicketPartLine>()
            .HasOne(l => l.WithdrawBatch)
            .WithMany(b => b.Lines)
            .HasForeignKey(l => l.WithdrawBatchId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TicketAttachment>()
            .HasOne(a => a.WithdrawBatch)
            .WithMany(b => b.Attachments)
            .HasForeignKey(a => a.WithdrawBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // AuditLog index for fast lookup by entity
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.EntityType, a.EntityId });

        // PartStock: one row per Part x Location, unique
        modelBuilder.Entity<PartStock>()
            .HasIndex(s => new { s.PartId, s.LocationId })
            .IsUnique();
        modelBuilder.Entity<PartStock>()
            .HasOne(s => s.Part)
            .WithMany(p => p.Stocks)
            .HasForeignKey(s => s.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PartStock>()
            .HasOne(s => s.Location)
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // PartUnit: individual serial-tracked units (optional). SerialNo unique.
        modelBuilder.Entity<PartUnit>()
            .HasIndex(u => u.SerialNo)
            .IsUnique();
        modelBuilder.Entity<PartUnit>()
            .HasOne(u => u.Part)
            .WithMany()
            .HasForeignKey(u => u.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PartUnit>()
            .HasOne(u => u.Location)
            .WithMany()
            .HasForeignKey(u => u.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        // StockMovement: ledger, indexed by part + time for reporting
        modelBuilder.Entity<StockMovement>()
            .HasIndex(m => new { m.PartNo, m.Timestamp });
        modelBuilder.Entity<StockMovement>()
            .HasIndex(m => new { m.PartId, m.Timestamp });
        // FK to Part — Restrict so a part with ledger history can't be hard-deleted (preserves audit trail)
        modelBuilder.Entity<StockMovement>()
            .HasOne(m => m.Part)
            .WithMany()
            .HasForeignKey(m => m.PartId)
            .OnDelete(DeleteBehavior.Restrict);
        // Optional FK to the specific serial-tracked unit
        modelBuilder.Entity<StockMovement>()
            .HasOne(m => m.PartUnit)
            .WithMany()
            .HasForeignKey(m => m.PartUnitId)
            .OnDelete(DeleteBehavior.SetNull);

        // GoodsReceipt / Lines
        modelBuilder.Entity<GoodsReceiptLine>()
            .HasOne(l => l.GoodsReceipt)
            .WithMany(g => g.Lines)
            .HasForeignKey(l => l.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Transaction tables → Part FK (Restrict — preserve history, prevent orphans) ──
        modelBuilder.Entity<GoodsReceiptLine>().HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReturnRequest>().HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockTransfer>().HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockCountLine>().HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DisposalRequest>().HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AtmModelPart>().HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<EquivalentGroupMember>().HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);

        // ReturnRequest → Ticket (required FK — enforces FR-RT-02 rule #1: every return traces to a ticket)
        modelBuilder.Entity<ReturnRequest>()
            .HasOne(r => r.Ticket)
            .WithMany()
            .HasForeignKey(r => r.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EquivalentPart>()
            .HasIndex(e => new { e.OriginalPartNo, e.EquivalentPartNo })
            .IsUnique();

        // EquivalentGroup: named groups with member PartNos
        modelBuilder.Entity<EquivalentGroupMember>()
            .HasIndex(m => new { m.GroupId, m.PartNo })
            .IsUnique();
        modelBuilder.Entity<EquivalentGroupMember>()
            .HasOne(m => m.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // AtmModel ↔ Part compatibility (many-to-many via AtmModelPart)
        modelBuilder.Entity<AtmModelPart>()
            .HasIndex(m => new { m.AtmModelId, m.PartNo })
            .IsUnique();
        modelBuilder.Entity<AtmModelPart>()
            .HasOne(m => m.AtmModel)
            .WithMany(a => a.CompatibleParts)
            .HasForeignKey(m => m.AtmModelId)
            .OnDelete(DeleteBehavior.Cascade);

        // User: unique email
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // StockCountLine: Variance is computed in C#, not stored
        modelBuilder.Entity<StockCountLine>()
            .Ignore(l => l.Variance);
        modelBuilder.Entity<StockCountLine>()
            .HasOne(l => l.StockCount)
            .WithMany(c => c.Lines)
            .HasForeignKey(l => l.StockCountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
