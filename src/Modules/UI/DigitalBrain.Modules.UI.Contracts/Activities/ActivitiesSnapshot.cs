namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.activities-snapshot")]
public sealed record ActivitiesSnapshot(
    [property: Id(0)] DateTimeOffset ObservedAt,
    [property: Id(1)] IReadOnlyList<ActivityView> Activities);
