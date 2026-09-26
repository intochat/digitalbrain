using System.Collections.Concurrent;

namespace IntoChat.Operations;

// A "Report a problem" entry always carries the intent id it was raised from, so support can join
// it to the intent's usage, trace and statement line.
internal sealed record ProblemReport(string IntentId, string WorkspaceId, string Message, DateTimeOffset ReportedAt);

internal interface IProblemReportStore
{
    Task<ProblemReport> AddAsync(string workspaceId, string intentId, string message, CancellationToken ct);

    Task<IReadOnlyList<ProblemReport>> ListAsync(string workspaceId, CancellationToken ct);
}

internal sealed class ProblemReportStore(TimeProvider? time = null) : IProblemReportStore
{
    private const int MaxMessageLength = 4_000;
    private readonly ConcurrentDictionary<string, List<ProblemReport>> _byWorkspace = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<ProblemReport> AddAsync(string workspaceId, string intentId, string message, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > MaxMessageLength)
        {
            throw new ArgumentException($"A problem report is limited to {MaxMessageLength} characters.", nameof(message));
        }

        var report = new ProblemReport(intentId, workspaceId, message, _time.GetUtcNow());
        var reports = _byWorkspace.GetOrAdd(workspaceId, static _ => []);
        lock (reports)
        {
            reports.Add(report);
        }

        return Task.FromResult(report);
    }

    public Task<IReadOnlyList<ProblemReport>> ListAsync(string workspaceId, CancellationToken ct)
    {
        if (!_byWorkspace.TryGetValue(workspaceId, out var reports))
        {
            return Task.FromResult<IReadOnlyList<ProblemReport>>([]);
        }

        lock (reports)
        {
            return Task.FromResult<IReadOnlyList<ProblemReport>>(reports.ToArray());
        }
    }
}
