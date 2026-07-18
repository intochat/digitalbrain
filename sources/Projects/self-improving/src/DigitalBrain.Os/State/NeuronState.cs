using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Os.State;

[GenerateSerializer]
public sealed class NeuronState
{
    [Id(0)]
    public List<BundleId> InstalledBundles { get; set; } = new();

    // Synapse history (v2-style Incoming/Outgoing for durability + replay potential; concrete List + redis IPersistentState for this slice).
    [Id(1)]
    public List<Synapse> Incoming { get; set; } = new();

    [Id(2)]
    public List<Synapse> Outgoing { get; set; } = new();

    [Id(3)]
    public Dictionary<string, string> CustomState { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Id(4)]
    public List<BundleId> PublishedBundles { get; set; } = new();

    [Id(5)]
    public Dictionary<string, string> Memory { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Contract-only bundles (private marketplace path): store decls so ListSubscribers and ListActive can account for promised handlers without shipping impl.
    // Keyed by bundle id; survives re-activation via journals/state.
    [Id(6)]
    public Dictionary<BundleId, ContractDeclaration[]> ContractBundles { get; set; } = new();

    [Id(7)]
    public string? BrainPublicKeyBase64 { get; set; }

    [Id(8)]
    public string? BrainPrivateKeyBase64 { get; set; }
}

public static class NeuronStateKeys
{
    public const string State = "neuron";
}