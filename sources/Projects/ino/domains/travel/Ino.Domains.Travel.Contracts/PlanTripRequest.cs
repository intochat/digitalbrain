using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// User-initiated intent to plan a multi-stop trip. Stub-only at this slice — CortexNeuron
/// routes <c>plan|trip|vacation</c> to this synapse, but no canonical handler is installed
/// until slice 8's ItineraryComposerNeuron. Until then the installability guard in Cortex
/// falls this through to <c>UnroutedIntent</c>. Slice 6 fleshes out the sibling contracts
/// (HotelsRequest, PlacesRequest, card responses).
/// </summary>
[GenerateSerializer]
public sealed record PlanTripRequest(
    [property: Id(0)] string Query) : ISynapse;
