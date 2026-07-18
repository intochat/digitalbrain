using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.FlightSearch;

/// <summary>
/// Three plausible flights for any prompt. v0.1 mock — real
/// tripradar-driven results land in a follow-up slice.
/// </summary>
internal static class MockFlightCorpus
{
    public static IReadOnlyList<FlightOption> For(string prompt) => new[]
    {
        new FlightOption("FL-001", "Singapore Airlines", "London", "Bali",  850m,  945),
        new FlightOption("FL-002", "Qatar Airways",      "London", "Bali",  720m, 1080),
        new FlightOption("FL-003", "Emirates",           "London", "Bali",  910m,  870),
    };
}
