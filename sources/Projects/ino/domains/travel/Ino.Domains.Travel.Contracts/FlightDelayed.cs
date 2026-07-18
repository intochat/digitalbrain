using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Reactive broadcast fired by FlightMonitorNeuron (slice 9) when a tracked
/// flight's schedule changes. Subscribers — ItineraryComposerNeuron, the
/// inspector timeline, and eventually a push-notification neuron — react to
/// update their own state. Reactive listeners react via <c>IReactsTo&lt;FlightDelayed&gt;</c>.
/// </summary>
[GenerateSerializer]
public sealed record FlightDelayed(
    [property: Id(0)] string FlightId,
    [property: Id(1)] string NewDepartTime,
    [property: Id(2)] string Reason) : ISynapse;
