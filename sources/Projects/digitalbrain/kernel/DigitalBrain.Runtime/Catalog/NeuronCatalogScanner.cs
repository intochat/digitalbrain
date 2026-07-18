using System.Reflection;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Catalog;

public sealed class NeuronCatalogScanner(
    IGrainFactory grains,
    ILogger<NeuronCatalogScanner> logger) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        var entries = ScanLoadedAssemblies();
        var catalog = grains.GetGrain<IBrainCatalog>("global");
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await catalog.RegisterAsync(entry);
        }
        logger.LogInformation("Registered {Count} neurons in BrainCatalog.", entries.Count);
    }

    public IReadOnlyList<NeuronCatalogEntry> ScanLoadedAssemblies()
        => Scan(AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeGetTypes));

    public IReadOnlyList<NeuronCatalogEntry> Scan(IEnumerable<Type> types)
    {
        var entries = new List<NeuronCatalogEntry>();
        foreach (var t in types)
        {
            if (!ImplementsAnyNeuronInterface(t)) continue;
            if (!typeof(INeuronMetadata).IsAssignableFrom(t))
            {
                // Orleans codegen emits a Proxy_* grain reference for every
                // neuron interface; those implement INeuron but never
                // INeuronMetadata. Skipping them is expected, not actionable —
                // ~25 per silo at startup. Keep it at Debug so a fresh boot
                // stays readable; the real misconfiguration case below stays
                // a Warning.
                logger.LogDebug("{Type} implements INeuron but not INeuronMetadata; skipping.", t.FullName);
                continue;
            }

            var idProperty = t.GetProperty(nameof(INeuronMetadata.Id), BindingFlags.Public | BindingFlags.Static);
            var iconProperty = t.GetProperty(nameof(INeuronMetadata.Icon), BindingFlags.Public | BindingFlags.Static);
            var capabilitiesProperty = t.GetProperty(nameof(INeuronMetadata.Capabilities), BindingFlags.Public | BindingFlags.Static);

            if (idProperty is null || iconProperty is null || capabilitiesProperty is null)
            {
                logger.LogWarning(
                    "{Type} declares INeuronMetadata but is missing one of the static abstract members (Id/Icon/Capabilities); skipping.",
                    t.FullName);
                continue;
            }

            var id = (NeuronId)idProperty.GetValue(null)!;
            var icon = (string)iconProperty.GetValue(null)!;
            var capabilities = (NeuronCapability)capabilitiesProperty.GetValue(null)!;
            var capabilityMarkers = CollectCapabilityMarkers(t);
            var handledSynapseTypes = CollectHandledSynapseTypes(t);

            entries.Add(new NeuronCatalogEntry(
                id, icon, capabilities, t.FullName!, capabilityMarkers, handledSynapseTypes,
                InferDomain(t.FullName!)));
        }
        return entries;
    }

    static bool ImplementsAnyNeuronInterface(Type t) =>
        !t.IsAbstract
        && (typeof(INeuron).IsAssignableFrom(t) || typeof(INeuronWithStringKey).IsAssignableFrom(t));

    static IReadOnlyList<string> CollectCapabilityMarkers(Type t) =>
        t.GetInterfaces()
            .Where(IsCapabilityMarker)
            .Select(i => i.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    static bool IsCapabilityMarker(Type i) =>
        i != typeof(INeuron)
        && i != typeof(INeuronWithStringKey)
        && (typeof(INeuron).IsAssignableFrom(i) || typeof(INeuronWithStringKey).IsAssignableFrom(i));

    static IReadOnlyList<string> CollectHandledSynapseTypes(Type t) =>
        t.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandle<>))
            .Select(i => i.GetGenericArguments()[0].FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    public static string InferDomain(string typeFullName)
    {
        if (string.IsNullOrEmpty(typeFullName)) return "system";

        const string DomainsPrefix = "DigitalBrain.Domains.";
        const string KernelPrefix = "DigitalBrain.Kernel.";
        const string SdkPrefix = "DigitalBrain.SDK.";

        if (typeFullName.StartsWith(SdkPrefix, StringComparison.Ordinal))
        {
            var rest = typeFullName.AsSpan(SdkPrefix.Length);
            var dot = rest.IndexOf('.');
            if (dot > 0)
            {
                var domain = rest[..dot].ToString().ToLowerInvariant();
                return domain == "sqlite" ? "data" : domain;
            }
            return "system";
        }

        if (typeFullName.StartsWith(DomainsPrefix, StringComparison.Ordinal))
        {
            var rest = typeFullName.AsSpan(DomainsPrefix.Length);
            var dot = rest.IndexOf('.');
            if (dot > 0) return rest[..dot].ToString().ToLowerInvariant();
            return "system";
        }

        if (typeFullName.StartsWith(KernelPrefix, StringComparison.Ordinal))
        {
            return "kernel";
        }

        return "system";
    }
}
