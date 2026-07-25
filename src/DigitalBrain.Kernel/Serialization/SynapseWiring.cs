using System.Collections.Concurrent;
using System.Reflection;

namespace DigitalBrain.Kernel;

public sealed record SynapseWiringEntry(string Neuron, string Synapse);

public sealed class DispatchManifest(IReadOnlyList<SynapseWiringEntry> handlers, IReadOnlyList<SynapseWiringEntry> emissions)
{
    public IReadOnlyList<SynapseWiringEntry> Handlers { get; } = handlers;

    public IReadOnlyList<SynapseWiringEntry> Emissions { get; } = emissions;
}

public static class SynapseWiring
{
    private const string GeneratedManifestType = "DigitalBrain.Generated.DispatchManifest";
    private const string GeneratedWiringsField = "Wirings";

    private static readonly ConcurrentDictionary<Assembly, DispatchManifest?> Manifests = new();

    public static bool TryGetManifest(Assembly assembly, out DispatchManifest manifest)
    {
        var found = Manifests.GetOrAdd(assembly, static probed => Load(probed));
        manifest = found ?? new DispatchManifest([], []);

        return found is not null;
    }

    private static DispatchManifest? Load(Assembly assembly)
    {
        if (assembly.GetType(GeneratedManifestType, throwOnError: false) is not { } generated)
        {
            return null;
        }

        if (generated.GetField(GeneratedWiringsField, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?
            .GetValue(null) is not IEnumerable<ValueTuple<string, string, bool>> wirings)
        {
            return null;
        }

        var handlers = new List<SynapseWiringEntry>();
        var emissions = new List<SynapseWiringEntry>();

        foreach (var (neuron, synapse, isHandler) in wirings)
        {
            (isHandler ? handlers : emissions).Add(new SynapseWiringEntry(neuron, synapse));
        }

        return new DispatchManifest(handlers, emissions);
    }
}
