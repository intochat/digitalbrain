using Ino.Core;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// FlightSearch's response synapse. Carries both the typed flight list (for
/// any consumer that wants the data) and pre-rendered RFW bytes (for the
/// gateway to ship to Flutter). Implements <see cref="IHasRfwPayload"/> so
/// the gateway extracts the bytes without knowing this is a Travel response.
/// </summary>
[GenerateSerializer]
public sealed record FlightCardResponse(
    [property: Id(0)] string Summary,
    // Typed as FlightSummary[] (not IReadOnlyList<FlightSummary>) so Orleans'
    // generated copier targets the concrete array type. With the interface
    // declaration, the copier walks runtime types — and collection-expression
    // initializers (`[..]` targeting IReadOnlyList<T>) materialize as
    // <>z__ReadOnlyArray<T>, a compiler-synthesized wrapper with no
    // registered copier — triggering CodecNotFoundException on cross-silo
    // deep-copy. Array field + array fixture keeps every codepath on T[].
    [property: Id(1)] FlightSummary[] Flights,
    [property: Id(2)] byte[] RfwDescription,
    [property: Id(3)] byte[] RfwData) : ISynapse, IHasRfwPayload
{
    /// <inheritdoc />
    public string ContentType => "flight_results";
}
