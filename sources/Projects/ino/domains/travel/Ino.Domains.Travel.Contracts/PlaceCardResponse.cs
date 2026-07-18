using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// PlaceSearch's response synapse. Concrete arrays to stay off the
/// <c>&lt;&gt;z__ReadOnlyArray&lt;T&gt;</c> codec trap on cross-silo deep-copy.
/// </summary>
[GenerateSerializer]
public sealed record PlaceCardResponse(
    [property: Id(0)] string Summary,
    [property: Id(1)] PlaceSummary[] Places,
    [property: Id(2)] byte[] RfwDescription,
    [property: Id(3)] byte[] RfwData) : ISynapse, IHasRfwPayload
{
    public string ContentType => "place_results";
}
