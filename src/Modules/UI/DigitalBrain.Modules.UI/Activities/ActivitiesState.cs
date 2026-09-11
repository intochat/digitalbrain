namespace DigitalBrain.UI;

[GenerateSerializer, Alias("db.ui.activities-state")]
internal sealed record ActivitiesState(
    [property: Id(0)] Dictionary<string, ActivityState> Activities)
{
    public const int MaxActivities = 256;

    public ActivitiesState WithActivity(string key, ActivityState activity)
    {
        var activities = new Dictionary<string, ActivityState>(Activities, StringComparer.Ordinal)
        {
            [key] = activity,
        };
        var oldest = activities.Where(pair => pair.Value.RootSettled)
            .OrderBy(pair => pair.Value.Operations.Values.Max(fact => fact.Timestamp))
            .Take(Math.Max(0, activities.Count - MaxActivities)).Select(pair => pair.Key).ToArray();
        // Unresolved roots must survive even when they alone exceed the cap.
        foreach (var evicted in oldest)
        {
            activities.Remove(evicted);
        }

        return new ActivitiesState(activities);
    }
}
