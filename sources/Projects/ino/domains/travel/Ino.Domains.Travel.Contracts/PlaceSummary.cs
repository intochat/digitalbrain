namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Single point of interest surfaced by PlaceSearch. Field names align 1:1 with
/// the Flutter <c>PlaceCard</c> widget's RFW bindings
/// (<c>clients/ino.flutter/lib/ui/components/place_card.dart</c>).
/// </summary>
[GenerateSerializer]
public sealed record PlaceSummary(
    [property: Id(0)] string Name,
    [property: Id(1)] string Type,
    [property: Id(2)] double Rating,
    [property: Id(3)] int ReviewCount,
    [property: Id(4)] string Description);
