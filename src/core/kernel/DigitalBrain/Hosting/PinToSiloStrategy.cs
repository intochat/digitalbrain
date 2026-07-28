using Orleans.Metadata;
using Orleans.Placement;

namespace DigitalBrain.Kernel;

internal sealed class PinToSiloStrategy : PlacementFilterStrategy
{
    private const string LabelProperty = "label";

    public PinToSiloStrategy()
        : base(order: 0)
    {
    }

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
