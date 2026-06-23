using Microsoft.EntityFrameworkCore;

namespace Api.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Part> Parts { get; set; }
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<Vendor> Vendors { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<PartStock> PartStocks { get; set; }
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Part.PartNo unique index
        modelBuilder.Entity<Part>()
            .HasIndex(p => p.PartNo)
            .IsUnique();

        // Part → Category (nullable FK, SetNull on delete)
        modelBuilder.Entity<Part>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Parts)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Ticket.RequestedPartNo stores the PartNo string (no FK constraint for SQLite compat)
        modelBuilder.Entity<Ticket>()
            .Property(t => t.RequestedPartNo)
            .IsRequired(false);

        modelBuilder.Entity<Ticket>()
            .Property(t => t.ApprovedPartNo)
            .IsRequired(false);

        // Ignore navigation properties — resolve via service layer instead
        modelBuilder.Entity<Ticket>()
            .Ignore(t => t.RequestedPartNav)
            .Ignore(t => t.ApprovedPartNav);

        // AuditLog index for fast lookup by entity
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.EntityType, a.EntityId });

        // PartStock: one row per Part x Location, unique
        modelBuilder.Entity<PartStock>()
            .HasIndex(s => new { s.PartId, s.LocationId })
            .IsUnique();
        modelBuilder.Entity<PartStock>()
            .HasOne(s => s.Part)
            .WithMany()
            .HasForeignKey(s => s.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PartStock>()
            .HasOne(s => s.Location)
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // StockMovement: ledger, indexed by part + time for reporting
        modelBuilder.Entity<StockMovement>()
            .HasIndex(m => new { m.PartNo, m.Timestamp });

        // GoodsReceipt / Lines
        modelBuilder.Entity<GoodsReceiptLine>()
            .HasOne(l => l.GoodsReceipt)
            .WithMany(g => g.Lines)
            .HasForeignKey(l => l.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

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
