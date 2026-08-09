namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record HostSnapshot(
    int AcceptedCount,
    int CommittedOutboxCount,
    IReadOnlyList<string> JournalKinds);
