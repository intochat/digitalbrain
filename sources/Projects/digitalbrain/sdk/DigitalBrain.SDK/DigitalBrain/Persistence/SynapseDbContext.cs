using Microsoft.EntityFrameworkCore;

namespace DigitalBrain.SDK.DigitalBrain.Persistence;

public class SynapseEntity
{
    public Guid SynapseId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid CallerNeuronId { get; set; }
    public string? CallerNeuronType { get; set; }
    public Guid ReceiverNeuronId { get; set; }
    public string ReceiverNeuronType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string? Traceparent { get; set; }
    public string? Tracestate { get; set; }
    
    // Serialized custom properties for dynamic/InoLang typed synapses
    public string? PayloadJson { get; set; }
}

public sealed class SynapseDbContext : DbContext
{
    public SynapseDbContext(DbContextOptions<SynapseDbContext> options) : base(options) { }

    public DbSet<SynapseEntity> Synapses => Set<SynapseEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SynapseEntity>(entity =>
        {
            entity.HasKey(s => s.SynapseId);
            entity.HasIndex(s => s.CorrelationId);
            entity.Property(s => s.ReceiverNeuronType).IsRequired();
        });
    }
}
