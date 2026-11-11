using DigitalBrain.Kernel;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Capabilities.Connections.Integrations;

public sealed record IntegrationCapability(string IntegrationId, string Kind, string Id, string Display);

// Capabilities are derived on every call from live registrations and accounts, never stored.
public interface ICapabilities
{
    Task<IntegrationCapability[]> List(CapabilityCaller caller, string? kind = null, CancellationToken ct = default);
}

// A module contributes the kinds of capability its integrations provide.
public interface ICapabilitySource
{
    Task<IntegrationCapability[]> Derive(CapabilityCaller caller, CancellationToken ct);
}

// Capabilities a brain has live beyond its kernel types (the Tools module's MCP and page tool sources,
// asked for their current tools). NeuronTypeSearch ranks them together with the kernel's neuron types.
public interface IRuntimeCapabilitySource
{
    Task<IReadOnlyList<NeuronType>> Read(string brainId, CancellationToken ct);
}

// A capability the brain can search and call. A kernel neuron type has no Key; a live tool (from an
// IRuntimeCapabilitySource) is bound to one neuron Key and names its Tool, called as Run(Tool, arguments).
public sealed record NeuronType(
    string Id,
    string ModuleId,
    string Name,
    string Description,
    IReadOnlyList<string> Methods,
    string Contract = "",
    string? Key = null,
    string? Tool = null)
{
    // A kernel neuron type as a searchable capability: no Key, called on any instance of it.
    public static NeuronType From(BrainType entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var name = entry.Name[(entry.Name.LastIndexOf('.') + 1)..];
        return new(entry.Alias, entry.Origin, name.StartsWith('I') ? name[1..] : name, entry.Description, entry.Shape, entry.Name);
    }
}

public sealed record NeuronTypeHit(NeuronType Type, double Score);
