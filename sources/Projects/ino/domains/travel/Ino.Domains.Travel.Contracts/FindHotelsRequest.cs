using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// User-initiated (or Cortex-routed) intent to find hotels for a stay. Slice 6
/// ships the contract; slice 7 adds HotelSearchNeuron with seeded fixtures;
/// slice 9 wires the real TripRadar HTTP call in place of fixtures.
/// </summary>
[GenerateSerializer]
public sealed record FindHotelsRequest(
    [property: Id(0)] string Query) : ISynapse;
