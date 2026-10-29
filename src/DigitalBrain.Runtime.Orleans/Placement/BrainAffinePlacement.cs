using Orleans;
using Orleans.Placement;
using Orleans.Runtime;
using Orleans.Runtime.Placement;

namespace DigitalBrain.Runtime.Orleans.Placement;

/// <summary>Places every grain of a brain on one silo. The grain key starts with the brain: <c>brain</c> or <c>brain/...</c>.</summary>
[Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking, Alias("brain.affine-placement")]
internal sealed class BrainAffinePlacement : PlacementStrategy;

/// <summary>Marks a grain class as brain-affine. Applied by the runtime to its own grains, never by neuron code.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
internal sealed class BrainAffinePlacementAttribute() : PlacementAttribute(new BrainAffinePlacement());

/// <summary>Every silo computes the same answer: FNV-1a of the brain id over the sorted compatible silos.</summary>
internal sealed class BrainAffinePlacementDirector : IPlacementDirector
{
    public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var silos = context.GetCompatibleSilos(target);
        if (silos.Length == 0) { throw new OrleansException($"No silo can host {target.GrainIdentity}."); }
        var sorted = silos.Order().ToArray();
        var brain = BrainOf(target.GrainIdentity.Key.ToString()!);
        return Task.FromResult(sorted[(int)(Fnv1a(brain) % (uint)sorted.Length)]);
    }

    internal static string BrainOf(string key)
    {
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? key : key[..slash];
    }

    // FNV-1a over UTF-16 code units: process-independent, unlike string.GetHashCode.
    internal static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value) { hash = (hash ^ c) * 16777619u; }
        return hash;
    }
}
