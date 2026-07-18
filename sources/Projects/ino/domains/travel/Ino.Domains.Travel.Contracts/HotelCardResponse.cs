using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// HotelSearch's response synapse. Carries the typed hotel list plus
/// pre-rendered RFW bytes for the gateway to ship to Flutter. Follows the
/// same shape as <see cref="FlightCardResponse"/>: concrete <c>T[]</c> arrays
/// (not <c>IReadOnlyList&lt;T&gt;</c>) to sidestep the <c>&lt;&gt;z__ReadOnlyArray&lt;T&gt;</c>
/// deep-copy codec gap on cross-silo dispatch.
/// </summary>
[GenerateSerializer]
public sealed record HotelCardResponse(
    [property: Id(0)] string Summary,
    [property: Id(1)] HotelSummary[] Hotels,
    [property: Id(2)] byte[] RfwDescription,
    [property: Id(3)] byte[] RfwData) : ISynapse, IHasRfwPayload
{
    public string ContentType => "hotel_results";
}
