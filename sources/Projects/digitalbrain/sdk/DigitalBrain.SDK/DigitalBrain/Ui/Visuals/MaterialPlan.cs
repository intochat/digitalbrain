namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record MaterialPlan(
    [property: Id(0)] string Surface,
    [property: Id(1)] double Sigma,
    [property: Id(2)] double Chromaticity,
    [property: Id(3)] double SpecPhaseHz,
    [property: Id(4)] double Refract,
    [property: Id(5)] uint ToneArgb,
    // "full" | "reduced" | "minimal" — kept as string so motion-lab presets don't require enum churn
    [property: Id(6)] string MotionPreset);
