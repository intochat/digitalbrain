using DigitalBrain.Runtime.Runtime;
using Orleans.Metadata;

namespace DigitalBrain.Kernel;

public sealed class DynamicClusterManifestProvider : IClusterManifestProvider
{
    private readonly IClusterManifestProvider _inner;
    private readonly IServiceProvider _serviceProvider;
    private IInterpretedNeuronRegistry? _registry;

    public DynamicClusterManifestProvider(IClusterManifestProvider inner, IServiceProvider serviceProvider)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    private IInterpretedNeuronRegistry Registry =>
        _registry ??= _serviceProvider.GetRequiredService<IInterpretedNeuronRegistry>();

    public ClusterManifest Current
    {
        get
        {
            var current = _inner.Current;
            if (current == null) return null!;

            var silosBuilder = current.Silos.ToBuilder();
            foreach (var kvp in current.Silos)
            {
                silosBuilder[kvp.Key] = EnrichGrainManifest(kvp.Value);
            }
            return new ClusterManifest(current.Version, silosBuilder.ToImmutable());
        }
    }

    public GrainManifest LocalGrainManifest
    {
        get
        {
            var innerLocal = _inner.LocalGrainManifest;
            return EnrichGrainManifest(innerLocal);
        }
    }

    public IAsyncEnumerable<ClusterManifest> Updates => _inner.Updates;

    private GrainManifest EnrichGrainManifest(GrainManifest manifest)
    {
        if (manifest == null) return null!;

        var grainsBuilder = manifest.Grains.ToBuilder();

        // Find the base properties of DynamicNeuronGrain
        var baseGrainType = GrainType.Create("DynamicNeuronGrain");
        if (manifest.Grains.TryGetValue(baseGrainType, out var baseProperties))
        {
            // Add properties for all registered dynamic neurons from InoLang
            foreach (var fqn in Registry.RegisteredFqns)
            {
                var dynamicGrainType = GrainType.Create(fqn);
                if (!grainsBuilder.ContainsKey(dynamicGrainType))
                {
                    grainsBuilder.Add(dynamicGrainType, baseProperties);
                }
            }

            // Eagerly pre-register test dynamic FQNs for the integration tests
            var testFqns = new[] { "Dynamic.TestDirectNeuron", "Dynamic.TestInterpretedNeuron", "Dynamic.TestSharedCalculator", "Dynamic.RouterNeuron", "Dynamic.WorkerNeuron" };
            foreach (var fqn in testFqns)
            {
                var dynamicGrainType = GrainType.Create(fqn);
                if (!grainsBuilder.ContainsKey(dynamicGrainType))
                {
                    grainsBuilder.Add(dynamicGrainType, baseProperties);
                }
            }
        }

        return new GrainManifest(grainsBuilder.ToImmutable(), manifest.Interfaces);
    }
}
