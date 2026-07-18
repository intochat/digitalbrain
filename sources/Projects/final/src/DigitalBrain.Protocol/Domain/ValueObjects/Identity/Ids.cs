using DigitalBrain.Protocol;

namespace DigitalBrain.Protocol.Domain.ValueObjects.Identity;

[GenerateSerializer]
public readonly record struct SynapseId(Guid Value)
{
    public static SynapseId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

[GenerateSerializer]
public readonly record struct CorrelationId(Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

[GenerateSerializer]
public readonly record struct CausationId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

[GenerateSerializer]
public readonly record struct NeuronId(string Type, string Key)
{
    public static readonly NeuronId None = new(string.Empty, string.Empty);

    public bool IsNone => string.IsNullOrEmpty(Type) && string.IsNullOrEmpty(Key);

    // Creates NeuronId from INeuron interface full name (removes string magic; interface = identity).
    public static NeuronId For<TNeuron>(string key) where TNeuron : INeuron
        => new(typeof(TNeuron).FullName ?? typeof(TNeuron).Name, key ?? string.Empty);

    public static NeuronId For(string neuronInterfaceFullName, string key)
        => new(neuronInterfaceFullName ?? string.Empty, key ?? string.Empty);

    public override string ToString() => IsNone ? "none" : $"{Type}/{Key}";
}

[GenerateSerializer]
public enum RoutingMode { PointToPoint, Broadcast }

[GenerateSerializer]
public enum BrainScope { LocalPrivate, Cluster, Global }

[GenerateSerializer]
public sealed record SynapseMetadata(
    [property: Id(0)] SynapseId SynapseId,
    [property: Id(1)] CorrelationId CorrelationId,
    [property: Id(2)] CausationId? CausationId,
    [property: Id(3)] NeuronId Caller,
    [property: Id(4)] NeuronId Receiver,
    [property: Id(5)] DateTimeOffset Timestamp,
    [property: Id(6)] RoutingMode RoutingMode = RoutingMode.PointToPoint,
    [property: Id(7)] BrainScope Scope = BrainScope.LocalPrivate);

// DDD value objects for first-class domain concepts (replacing stringly-typed ids in synapses + state).
// Follows exact pattern of NeuronId etc: readonly record struct + [GenerateSerializer] for Orleans/persistence.
// Primary ctor gives implicit member Id(0) per serialization docs; implicit string conversions for thin REPL/compat during transition.
// Self-explanatory, nominal safety for experiences/bundles/tasks, version-tolerant, flows into List<T> in NeuronState + Synapse payloads.

[GenerateSerializer]
public readonly record struct ExperienceId([property: Id(0)] string Value)
{
    public static ExperienceId From(string value) => new(value ?? string.Empty);
    public static implicit operator ExperienceId(string value) => From(value);
    public static implicit operator string(ExperienceId id) => id.Value;
    public override string ToString() => Value;
}

[GenerateSerializer]
public readonly record struct BundleId([property: Id(0)] string Value)
{
    public static BundleId From(string value) => new(value ?? string.Empty);
    public static implicit operator BundleId(string value) => From(value);
    public static implicit operator string(BundleId id) => id.Value;
    public override string ToString() => Value;
}

[GenerateSerializer]
public readonly record struct TaskId([property: Id(0)] string Value)
{
    public static TaskId From(string value) => new(value ?? string.Empty);
    public static implicit operator TaskId(string value) => From(value);
    public static implicit operator string(TaskId id) => id.Value;
    public override string ToString() => Value;
}

[GenerateSerializer]
public sealed record BrainDescriptor(
    [property: Id(0)] string Name,
    [property: Id(1)] string Kind,
    [property: Id(2)] string World,
    [property: Id(3)] string Host,
    [property: Id(4)] int GatewayPort,
    [property: Id(5)] DateTimeOffset CreatedAt,
    [property: Id(6)] DateTimeOffset LastActive,
    [property: Id(7)] bool Archived,
    [property: Id(8)] string? PublicKeyBase64 = null,
    [property: Id(9)] string? Fingerprint = null);

[GenerateSerializer]
public sealed record BrainIdentity(
    [property: Id(0)] string PublicKeyBase64,
    [property: Id(1)] string Fingerprint,
    [property: Id(2)] DateTimeOffset CreatedAt);
