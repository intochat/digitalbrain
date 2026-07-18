namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Single hotel option surfaced by HotelSearch. Field names align 1:1 with the
/// Flutter <c>HotelCard</c> widget's RFW bindings
/// (<c>clients/ino.flutter/lib/ui/components/hotel_card.dart</c>), so the
/// template shipping this data can pass fields through without renaming.
/// </summary>
[GenerateSerializer]
public sealed record HotelSummary(
    [property: Id(0)] string Name,
    [property: Id(1)] string Location,
    [property: Id(2)] int PricePerNightUsd,
    [property: Id(3)] double Rating,
    [property: Id(4)] int Stars,
    [property: Id(5)] int Nights);
