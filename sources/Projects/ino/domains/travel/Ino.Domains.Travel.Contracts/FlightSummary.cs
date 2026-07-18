namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Single flight option surfaced by FlightSearch. Fields map 1:1 onto the
/// FlightCard RFW template bindings in
/// <c>Ino.Domains.Travel.UI.FlightCardTemplate</c>.
/// </summary>
[GenerateSerializer]
public sealed record FlightSummary(
    [property: Id(0)] string Airline,
    [property: Id(1)] string FromCode,
    [property: Id(2)] string ToCode,
    [property: Id(3)] int PriceUsd,
    [property: Id(4)] string DepartTime,
    [property: Id(5)] string Duration);
