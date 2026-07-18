namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record MaterialOverride(
    [property: Id(0)] double? Sigma,
    [property: Id(1)] double? Chromaticity,
    [property: Id(2)] double? SpecPhaseHz,
    [property: Id(3)] double? Refract,
    [property: Id(4)] uint? ToneArgb,
    [property: Id(5)] string? MotionPreset)
{
    public static readonly MaterialOverride None = new(null, null, null, null, null, null);

    public bool IsEmpty => Sigma is null && Chromaticity is null && SpecPhaseHz is null
                       && Refract is null && ToneArgb is null && MotionPreset is null;
}
