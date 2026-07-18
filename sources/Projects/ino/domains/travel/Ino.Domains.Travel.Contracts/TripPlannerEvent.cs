using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Discriminated union of journal events recorded by <c>TripPlannerNeuron</c>.
/// The neuron derives its slot-fill state from the journal on each turn — no
/// projected state record. Implements <see cref="ISynapse"/> so it satisfies
/// the <c>where TEvent : class, ISynapse</c> constraint on
/// <see cref="Ino.Core.Hosting.Neuron{TEvent}"/>.
/// </summary>
[GenerateSerializer]
public abstract record TripPlannerEvent : ISynapse;

/// <summary>Recorded once per conversation when the user fires <c>PlanTripRequest</c>.</summary>
[GenerateSerializer]
public sealed record TripPlanningStarted(
    [property: Id(0)] string Query) : TripPlannerEvent;

/// <summary>Recorded each time a slot value is filled — either from initial
/// query parsing or from a follow-up <c>ProvideClarification</c>.</summary>
[GenerateSerializer]
public sealed record SlotFilled(
    [property: Id(0)] string Field,
    [property: Id(1)] string Value) : TripPlannerEvent;
