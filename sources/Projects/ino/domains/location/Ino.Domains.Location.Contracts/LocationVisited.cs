using Ino.Core;

namespace Ino.Domains.Location.Contracts;

/// <summary>
/// Journaled by <c>LocationNeuron</c> every time the user is observed at a place.
/// Plans that need "home", "office", or current-location read this neuron's
/// event log via <see cref="Ino.Core.Hosting.IJournaledNeuronQuery{TEvent}"/>
/// and apply <see cref="Ino.Core.Hosting.RecallQuery{TEvent}"/> filters
/// (frequency aggregation for "home", recency for current location, label
/// match for explicit anchors).
/// </summary>
[GenerateSerializer]
public sealed record LocationVisited(
    [property: Id(0)] string Place,
    [property: Id(1)] string? Label) : ISynapse;
