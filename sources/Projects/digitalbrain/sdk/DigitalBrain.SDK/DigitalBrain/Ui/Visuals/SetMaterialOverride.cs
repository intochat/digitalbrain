using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record SetMaterialOverride([property: Id(1)] string ClientId,
    [property: Id(2)] string Surface,
    [property: Id(3)] MaterialOverride Patch
) : Synapse;
