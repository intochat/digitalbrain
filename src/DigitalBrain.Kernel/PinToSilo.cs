using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.Placement;
using Orleans.Runtime.MembershipService.SiloMetadata;
using Orleans.Runtime.Placement;

namespace DigitalBrain.Kernel;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PinToSiloAttribute(string label) : PlacementFilterAttribute(new PinToSiloStrategy(label))
{
    public string Label { get; } = label;
}

public sealed class PinToSiloStrategy : PlacementFilterStrategy
{
    private const string LabelProperty = "label";

    public PinToSiloStrategy()
        : base(order: 0)
    {
    }

    public PinToSiloStrategy(string label)
        : base(order: 0)
        => Label = label;

    public string Label { get; private set; } = string.Empty;

    public override void AdditionalInitialize(GrainProperties properties)
        => Label = GetPlacementFilterGrainProperty(LabelProperty, properties) ?? string.Empty;

    protected override IEnumerable<KeyValuePair<string, string>> GetAdditionalGrainProperties(
        IServiceProvider services,
        Type grainClass,
        GrainType grainType,
        IReadOnlyDictionary<string, string> existingProperties)
        => [new(LabelProperty, Label)];
}

internal sealed class PinToSiloDirector(ISiloMetadataCache metadata) : IPlacementFilterDirector
{
    internal const string SiloLabelKey = "db.silo";

    public IEnumerable<SiloAddress> Filter(
        PlacementFilterStrategy strategy,
        PlacementTarget target,
        IEnumerable<SiloAddress> silos)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        var required = ((PinToSiloStrategy)strategy).Label;

        return silos.Where(silo =>
            metadata.GetSiloMetadata(silo).Metadata.TryGetValue(SiloLabelKey, out var label)
            && string.Equals(label, required, StringComparison.Ordinal));
    }
}

public static class PinToSiloExtensions
{
    public static IServiceCollection AddPinToSiloPlacement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddPlacementFilter<PinToSiloStrategy, PinToSiloDirector>(ServiceLifetime.Transient);
    }
}
