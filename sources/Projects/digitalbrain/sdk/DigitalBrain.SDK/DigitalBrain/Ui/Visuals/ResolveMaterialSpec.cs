using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record ResolveMaterialSpec([property: Id(1)] string ClientId,
    [property: Id(2)] string Surface,
    [property: Id(3)] string Tier,
    [property: Id(4)] string ThemeBrightness
) : Synapse;
