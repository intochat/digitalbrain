namespace DigitalBrain.UI;

/// <summary>Reads the most recently updated activities.</summary>
[GenerateSerializer]
[Alias("ui.read-activities")]
public sealed record ReadActivities(
    [property: Id(0)] int Limit = 100);
