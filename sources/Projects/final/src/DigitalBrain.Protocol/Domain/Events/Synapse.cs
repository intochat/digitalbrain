using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using IdentityCorrelationId = DigitalBrain.Protocol.Domain.ValueObjects.Identity.CorrelationId;
using IdentitySynapseId = DigitalBrain.Protocol.Domain.ValueObjects.Identity.SynapseId;

namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public abstract record Synapse
{
    [Id(0)]
    public SynapseMetadata Metadata { get; init; } = CreateDefaultMetadata();

    public Guid SynapseId => Metadata.SynapseId.Value;
    public Guid CorrelationId => Metadata.CorrelationId.Value;
    public Guid? CausationId => Metadata.CausationId?.Value;

    public Guid CallerNeuronId => ParseKey(Metadata.Caller.Key);
    public string? CallerNeuronType => Metadata.Caller.Type;

    public Guid ReceiverNeuronId => ParseKey(Metadata.Receiver.Key);
    public string ReceiverNeuronType => Metadata.Receiver.Type ?? string.Empty;

    public DateTimeOffset Timestamp => Metadata.Timestamp;
    public RoutingMode RoutingMode => Metadata.RoutingMode;
    public BrainScope Scope => Metadata.Scope;

    protected Synapse() { }
    protected Synapse(SynapseMetadata meta) => Metadata = meta;

    public Synapse Stamp(NeuronId firing, Synapse? incoming = null)
    {
        var m = Metadata;
        var corr = incoming is not null && incoming.CorrelationId != default ? incoming.Metadata.CorrelationId : (m.CorrelationId.Value != default ? m.CorrelationId : IdentityCorrelationId.New());
        var caus = m.CausationId ?? (incoming is null ? null : new DigitalBrain.Protocol.Domain.ValueObjects.Identity.CausationId(incoming.SynapseId));
        var recv = m.Receiver.IsNone && incoming is not null ? incoming.Metadata.Caller : m.Receiver;
        var ts = m.Timestamp == default ? DateTimeOffset.UtcNow : m.Timestamp;
        var sc = m.Scope == default ? (incoming?.Scope ?? BrainScope.LocalPrivate) : m.Scope;

        return this with
        {
            Metadata = new SynapseMetadata(m.SynapseId, corr, caus, m.Caller.IsNone ? firing : m.Caller, recv, ts, m.RoutingMode, sc)
        };
    }

    // A NeuronId key that is not a Guid (e.g. a well-known string key) has no Guid identity — return Empty,
    // never a fresh random Guid, so the same synapse always reports the same caller/receiver id for tracing.
    private static Guid ParseKey(string? key) =>
        !string.IsNullOrEmpty(key) && Guid.TryParse(key, out var g) ? g : Guid.Empty;

    private static SynapseMetadata CreateDefaultMetadata() => new(
        IdentitySynapseId.New(),
        IdentityCorrelationId.New(),
        null,
        NeuronId.None,
        NeuronId.None,
        DateTimeOffset.UtcNow);
}
