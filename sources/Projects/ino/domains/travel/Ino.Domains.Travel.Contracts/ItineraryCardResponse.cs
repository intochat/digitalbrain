using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// ItineraryComposerNeuron's composed response after fanning out to
/// HotelSearch, PlaceSearch, and FlightSearch. Holds a summary string plus
/// the day-by-day breakdown, and ships the pre-rendered RFW for Flutter to
/// paint the itinerary card. Concrete arrays throughout per the
/// cross-silo codec guidance on <see cref="FlightCardResponse"/>.
/// </summary>
[GenerateSerializer]
public sealed record ItineraryCardResponse(
    [property: Id(0)] string Summary,
    [property: Id(1)] string Destination,
    [property: Id(2)] ItineraryDay[] Days,
    [property: Id(3)] byte[] RfwDescription,
    [property: Id(4)] byte[] RfwData) : ISynapse, IHasRfwPayload
{
    public string ContentType => "itinerary";
}
