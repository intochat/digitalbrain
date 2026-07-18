using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// User-initiated (or Cortex-routed) intent to surface points of interest
/// (restaurants, attractions, landmarks) for a destination. Slice 7 adds
/// PlaceSearchNeuron with seeded fixtures.
/// </summary>
[GenerateSerializer]
public sealed record FindPlacesRequest(
    [property: Id(0)] string Query) : ISynapse;
