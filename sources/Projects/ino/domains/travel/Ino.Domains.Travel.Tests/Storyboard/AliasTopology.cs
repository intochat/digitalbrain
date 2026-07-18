namespace Ino.Domains.Travel.Tests.Storyboard;

// Storyboard alias → Orleans grain type name substring (lowercased;
// matches what Orleans embeds in GrainId.ToString()). Aligned with
// clients/ino.flutter/lib/screens/brain/brain_topology.dart _grainTypeToNodeId.
//
// NOTE: BrainTraceFilter always emits FromGrain = "" (RuntimeContext is
// internal to Orleans). Assertions therefore check ToGrain.Contains(alias)
// rather than a directed edge. This is a known gap documented in
// BrainTraceFilter.cs and will be revisited when Orleans exposes the
// calling grain identity in IIncomingGrainCallContext.
public static class AliasTopology
{
    public static readonly IReadOnlyDictionary<string, string> AliasToGrain =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Cortex",       "cortexneuron" },
            { "PlanTrip",     "tripplannerneuron" },
            { "FindFlights",  "flightsearchneuron" },
            { "FindHotels",   "hotelsearchneuron" },
            { "FindPlaces",   "placesearchneuron" },
            { "Preferences",  "recallneuron" },
            { "Forecast",     "locationneuron" },
            { "VisaReminder", "remindersneuron" },
        };

    public static string? ResolveGrainType(string alias) =>
        AliasToGrain.TryGetValue(alias, out var grain) ? grain : null;
}
