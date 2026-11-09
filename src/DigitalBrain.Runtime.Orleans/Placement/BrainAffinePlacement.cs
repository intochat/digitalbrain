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

/// <summary>Every silo computes the same answer: 64-bit FNV-1a Highest Random Weight (HRW) Rendezvous Hashing of the brain id and silo address.</summary>
internal sealed class BrainAffinePlacementDirector : IPlacementDirector
{
    public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var silos = context.GetCompatibleSilos(target);
        if (silos.Length == 0) { throw new OrleansException($"No silo can host {target.GrainIdentity}."); }
        if (silos.Length == 1) { return Task.FromResult(silos[0]); }

        var brain = BrainOf(target.GrainIdentity.Key.ToString()!);
        SiloAddress bestSilo = silos[0];
        ulong highestWeight = 0;

        foreach (var silo in silos)
        {
            var weight = ComputeWeight(brain, silo.ToParsableString());
            if (weight > highestWeight)
            {
                highestWeight = weight;
                bestSilo = silo;
            }
        }

        return Task.FromResult(bestSilo);
    }

    internal static string BrainOf(string key)
    {
        if (DigitalBrain.Identity.CompoundGrainKey.TryParse(key, out var compound))
        {
            return compound.Brain.Value;
        }

        var slash = key.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? key : key[..slash];
    }

    // 64-bit FNV-1a with SplitMix64 avalanche mixer for Highest Random Weight (HRW) Rendezvous Hashing
    internal static ulong ComputeWeight(string brainId, string siloAddress)
    {
        ulong hash = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        foreach (var c in brainId) { hash = (hash ^ c) * prime; }
        hash = (hash ^ ':') * prime;
        foreach (var c in siloAddress) { hash = (hash ^ c) * prime; }

        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;
        hash *= 0xc4ceb9fe1a85ec53UL;
        hash ^= hash >> 33;

        return hash;
    }
}
