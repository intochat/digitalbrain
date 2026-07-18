using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// User-initiated intent to find flights. Slice 1.2 passes the raw query
/// through so FlightSearch can keyword-match against seeded data. Slice 4
/// replaces the free-text query with structured fields (<c>Origin</c>,
/// <c>Destination</c>, <c>DepartDate</c>) parsed by a Cortex-style LLM router.
/// </summary>
[GenerateSerializer]
public sealed record FindFlightsRequest(
    [property: Id(0)] string Query) : ISynapse;
