using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Tells <c>FlightMonitorNeuron</c> to start watching a specific flight. Canonical
/// fire: one Monitor activation per <see cref="FlightId"/>, armed on demand. A later
/// slice will have <c>FlightSearchNeuron</c> fire this for each flight it returns,
/// stitching the monitor into the trip flow automatically; slice 9 ships the neuron
/// + contract so explicit arming works from tests and the demo button.
/// </summary>
[GenerateSerializer]
public sealed record ArmFlightMonitor(
    [property: Id(0)] string FlightId,
    [property: Id(1)] string Route,
    [property: Id(2)] TimeSpan TickInterval) : ISynapse;
