namespace DigitalBrain.Marketplace;

// Deterministic sanctions fake: a country on the list is a hit. A real screener implements the same
// interface behind G-1/G-3; no code calls a list provider directly.
internal sealed class FakeSanctionsScreener : ISanctionsScreener
{
    internal static readonly FakeSanctionsScreener Instance = new();

    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "IR", "KP", "SY", "CU",
    };

    public Task<ScreeningResult> ScreenAsync(ScreeningRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(Blocked.Contains(request.Country)
            ? new ScreeningResult { Cleared = false, Reason = $"Sanctions screening blocked country {request.Country}." }
            : new ScreeningResult { Cleared = true });
    }
}
