using DigitalBrain.Contracts;

namespace DigitalBrain.Registry;

[Alias("registry"), Orleans.Metadata.DefaultGrainType("registry")]
public interface IRegistry : INeuron
{
    // The one registry per brain lives under its own module key.
    const string Key = "registry";

    Task<IReadOnlyList<NeuronType>> Types();
    Task<IReadOnlyList<NeuronTypeHit>> Search(string query, int take = 10, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NeuronInstance>> Instances(string? typeId = null, bool activeOnly = false, int skip = 0, int take = 100);
}

[GenerateSerializer, Alias("registry.neuron-type")]
public sealed record NeuronType(
    [property: Id(0)] string Id,
    [property: Id(1)] string ModuleId,
    [property: Id(2)] string Name,
    [property: Id(3)] string Description,
    [property: Id(4)] IReadOnlyList<string> Methods,
    [property: Id(5)] string Contract = "",
    [property: Id(6)] IReadOnlyList<string>? Signals = null)
{
    // "DigitalBrain.Time.TimeModule" reads as module id "time".
    public static string ModuleIdOf(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        var name = moduleType.Name;
        return (name.EndsWith("Module", StringComparison.Ordinal) ? name[..^"Module".Length] : name).ToLowerInvariant();
    }
}

[GenerateSerializer, Alias("registry.type-hit")]
public sealed record NeuronTypeHit([property: Id(0)] NeuronType Type, [property: Id(1)] double Score);

// One observed identity, not an event log or an authoritative liveness record.
[GenerateSerializer, Alias("registry.neuron-instance")]
public sealed record NeuronInstance(
    [property: Id(0)] string Id,
    [property: Id(1)] string Key,
    [property: Id(2)] IReadOnlyList<string> TypeIds,
    [property: Id(3)] Guid ActivationId,
    [property: Id(4)] DateTimeOffset FirstSeenAt,
    [property: Id(5)] DateTimeOffset LastSeenAt,
    [property: Id(6)] bool LastKnownActive);
