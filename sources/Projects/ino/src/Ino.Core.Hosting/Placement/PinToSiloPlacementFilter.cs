using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.Placement;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService.SiloMetadata;
using Orleans.Runtime.Placement;

namespace Ino.Core.Hosting.Placement;

// Pins a grain to the silo tagged with UseSiloMetadata["ino.silo"] == siloName,
// regardless of which silo made the call.
//
// Orleans' built-in [RequiredMatchSiloMetadata] filters by the CALLING silo's
// metadata (locality-aware), so it can't force all callers to converge on one
// silo for a cluster-singleton grain. This filter is absolute: it narrows
// candidates to silos whose metadata key "ino.silo" equals the configured
// target, independent of caller context. Intended for cluster-singleton grains
// like Discovery that must activate on a specific silo.
//
// The siloName travels through the Orleans grain manifest as a grain property
// (written by PinToSiloAttribute.Populate, read back by PinToSiloStrategy.
// AdditionalInitialize). This is the pattern used by the built-in
// RequiredMatch / PreferredMatch filters — instances of the strategy are
// reconstructed on each silo via the new() constraint on AddPlacementFilter,
// so state must flow through grain properties, not instance fields.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PinToSiloAttribute : PlacementFilterAttribute
{
    public string SiloName { get; }

    public PinToSiloAttribute(string siloName, int order = 0)
        : base(new PinToSiloStrategy(siloName, order))
    {
        SiloName = siloName;
    }

    public override void Populate(
        IServiceProvider services,
        Type grainClass,
        GrainType grainType,
        Dictionary<string, string> properties)
    {
        base.Populate(services, grainClass, grainType, properties);
        properties[PinToSiloStrategy.SiloNameProperty] = SiloName;
    }
}

public sealed class PinToSiloStrategy : PlacementFilterStrategy
{
    // Key used in UseSiloMetadata() to tag each silo with its role.
    public const string SiloMetadataKey = "ino.silo";

    // Grain-property key used to carry the target silo name from the attribute
    // through the manifest to the director. Must be distinct from SiloMetadataKey
    // — one tags silos, the other tags grains.
    public const string SiloNameProperty = "ino.placement.pin-to-silo";

    public string SiloName { get; private set; } = string.Empty;

    // Parameterless ctor required by AddPlacementFilter's new() constraint.
    public PinToSiloStrategy() : base(0) { }

    public PinToSiloStrategy(string siloName, int order) : base(order)
    {
        SiloName = siloName;
    }

    public override void AdditionalInitialize(GrainProperties properties)
    {
        base.AdditionalInitialize(properties);
        if (properties.Properties.TryGetValue(SiloNameProperty, out var name))
            SiloName = name;
    }
}

internal sealed class PinToSiloDirector(ISiloMetadataCache cache) : IPlacementFilterDirector
{
    public IEnumerable<SiloAddress> Filter(
        PlacementFilterStrategy filterStrategy,
        PlacementTarget target,
        IEnumerable<SiloAddress> silos)
    {
        var pin = (PinToSiloStrategy)filterStrategy;
        return silos.Where(silo =>
        {
            var metadata = cache.GetSiloMetadata(silo);
            return metadata is not null
                && metadata.Metadata.TryGetValue(PinToSiloStrategy.SiloMetadataKey, out var value)
                && value == pin.SiloName;
        });
    }
}

public static class PinToSiloServiceCollectionExtensions
{
    public static IServiceCollection AddPinToSiloPlacement(this IServiceCollection services) =>
        services.AddPlacementFilter<PinToSiloStrategy, PinToSiloDirector>(ServiceLifetime.Singleton);
}
