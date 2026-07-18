namespace DigitalBrain.SDK.Google.Gmail;

// Deterministic fast-stage stub registered when DigitalBrain:Google:UseStubServices=true.
// Returns the first N senders from a fixed list matching the M2 reference
// scenario in 06-milestone-m2-inbox.md. No external calls, no priming needed.
public sealed class StubGmailService : IGmailService
{
    static readonly IReadOnlyList<GmailSender> Fixture =
    [
        new GmailSender("Bob Builder",   "bob@example.com",   DateTimeOffset.Parse("2026-05-09T10:00:00Z"), "Build it"),
        new GmailSender("Carol King",    "carol@example.com", DateTimeOffset.Parse("2026-05-09T09:00:00Z"), "Lunch?"),
        new GmailSender("Dan Wong",      "dan@example.com",   DateTimeOffset.Parse("2026-05-09T08:00:00Z"), "Update"),
        new GmailSender("Eve White",     "eve@example.com",   DateTimeOffset.Parse("2026-05-09T07:00:00Z"), "Heads up"),
        new GmailSender("Frank Lee",     "frank@example.com", DateTimeOffset.Parse("2026-05-09T06:00:00Z"), "Tomorrow"),
    ];

    public Task<IReadOnlyList<GmailSender>> ListRecentSendersAsync(
        string userAccountId, int n, CancellationToken ct)
    {
        var take = Math.Clamp(n, 0, Fixture.Count);
        return Task.FromResult<IReadOnlyList<GmailSender>>(Fixture.Take(take).ToArray());
    }
}
