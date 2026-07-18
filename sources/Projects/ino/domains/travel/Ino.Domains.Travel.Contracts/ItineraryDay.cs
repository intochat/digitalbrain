namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Single day inside an itinerary — a day number, a title, and the list of
/// activity/booking line items for that day. <see cref="Items"/> is a
/// concrete array to stay off the <c>&lt;&gt;z__ReadOnlyArray&lt;T&gt;</c> codec trap.
/// </summary>
[GenerateSerializer]
public sealed record ItineraryDay(
    [property: Id(0)] int DayNumber,
    [property: Id(1)] string Title,
    [property: Id(2)] string[] Items);
