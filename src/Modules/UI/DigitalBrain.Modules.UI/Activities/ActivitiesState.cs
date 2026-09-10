namespace DigitalBrain.UI;

[GenerateSerializer, Alias("db.ui.activities-state")]
internal sealed record ActivitiesState(
    [property: Id(0)] Dictionary<string, ActivityState> Activities);
