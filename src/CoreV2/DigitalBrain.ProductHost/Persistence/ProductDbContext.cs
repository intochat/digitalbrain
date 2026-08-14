using Microsoft.EntityFrameworkCore;
using Brain.Abstractions.Activities;

namespace DigitalBrain.ProductHost.Persistence;

public sealed class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    internal DbSet<ProductStoreRecord> Records => Set<ProductStoreRecord>();

    internal DbSet<ProductActivityRecord> Activities => Set<ProductActivityRecord>();

    internal DbSet<ProductDeliveryRecord> Deliveries => Set<ProductDeliveryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<ProductStoreRecord>(entity =>
        {
            entity.ToTable("product_records");
            entity.HasKey(static record => record.Id);
            entity.Property(static record => record.Workspace).HasMaxLength(256).IsRequired();
            entity.Property(static record => record.Kind).HasMaxLength(128).IsRequired();
            entity.Property(static record => record.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(static record => record.UpdatedAt).IsRequired();
            entity.HasIndex(static record => new { record.Workspace, record.Kind });
        });
        modelBuilder.Entity<ProductActivityRecord>(entity =>
        {
            entity.ToTable("product_activities");
            entity.HasKey(static record => record.Id);
            entity.HasIndex(static record => new
            {
                record.Workspace,
                record.Principal,
                record.IdempotencyKey,
            }).IsUnique();
            entity.Property(static record => record.Workspace).HasMaxLength(256).IsRequired();
            entity.Property(static record => record.Principal).HasMaxLength(256).IsRequired();
            entity.Property(static record => record.Operation).HasMaxLength(256).IsRequired();
            entity.Property(static record => record.IdempotencyKey).HasMaxLength(512).IsRequired();
            entity.Property(static record => record.InputFingerprint).HasMaxLength(128).IsRequired();
            entity.Property(static record => record.TerminalResultContract).HasMaxLength(256).IsRequired();
        });
        modelBuilder.Entity<ProductDeliveryRecord>(entity =>
        {
            entity.ToTable("product_deliveries");
            entity.HasKey(static record => new { record.Workspace, record.DeliveryKey });
            entity.Property(static record => record.Workspace).HasMaxLength(256).IsRequired();
            entity.Property(static record => record.DeliveryKey).HasMaxLength(512).IsRequired();
            entity.Property(static record => record.PayloadReference).HasMaxLength(1024).IsRequired();
        });
    }
}

internal sealed class ProductActivityRecord
{
    public Guid Id { get; set; }
    public required string Workspace { get; set; }
    public required string Principal { get; set; }
    public required string Operation { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string InputFingerprint { get; set; }
    public required string TerminalResultContract { get; set; }
    public ActivityStatus Status { get; set; }
    public string? ResultContract { get; set; }
    public string? ResultPayloadReference { get; set; }
    public string? ProblemCode { get; set; }
    public string? ProblemSummary { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class ProductDeliveryRecord
{
    public required string DeliveryKey { get; set; }
    public required string Workspace { get; set; }
    public required string PayloadReference { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class ProductStoreRecord
{
    public Guid Id { get; set; }

    public required string Workspace { get; set; }

    public required string Kind { get; set; }

    // Product records contain redacted projections and opaque references only. Provider
    // credential material is deliberately absent from the relational model.
    public required string Payload { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
