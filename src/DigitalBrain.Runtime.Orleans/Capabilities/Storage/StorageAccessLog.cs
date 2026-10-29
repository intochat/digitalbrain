using System.Diagnostics;
using DigitalBrain.Capabilities.Storage;

namespace DigitalBrain.Runtime.Orleans.Capabilities.Storage;

/// <summary>One recorded storage call. Item ids that look like filesystem paths are stored as <c>redacted</c>.</summary>
internal sealed record StorageAccessRow(
    string App,
    string Brain,
    string ModuleKind,
    string ConnectionKey,
    string? ItemId,
    string Op,
    string Outcome,
    DateTimeOffset At);

/// <summary>
/// Append-only access log for storage@1. Rows stay in memory for the process.
/// <see cref="Journal"/> is the brain-journal port: the host sets it to append the row onto <c>BrainLog</c>.
/// The row never carries an absolute path or an exception message.
/// </summary>
internal sealed class StorageAccessLog
{
    internal static readonly ActivitySource Activity = new("DigitalBrain.Storage");

    private readonly List<StorageAccessRow> _rows = [];

    internal IReadOnlyList<StorageAccessRow> Rows
    {
        get { lock (_rows) { return [.. _rows]; } }
    }

    internal void Clear()
    {
        lock (_rows) { _rows.Clear(); }
    }

    /// <summary>Invoked after each row is kept. The host binds this to the brain journal.</summary>
    internal Action<StorageAccessRow>? Journal { get; set; }

    internal static void Stamp(StorageAccessLog log, string app, string brain, string moduleKind, string connectionKey, ItemId? item, string op, string outcome)
        => log.Add(app, brain, moduleKind, connectionKey, item, op, outcome);

    internal void Add(string app, string brain, string moduleKind, string connectionKey, ItemId? item, string op, string outcome)
    {
        var row = new StorageAccessRow(app, brain, moduleKind, connectionKey, Shown(item), op, outcome, DateTimeOffset.UtcNow);
        lock (_rows) { _rows.Add(row); }
        using var activity = Activity.StartActivity("storage.access");
        activity?.SetTag("storage.op", op);
        activity?.SetTag("storage.outcome", outcome);
        activity?.SetTag("storage.app", app);
        Journal?.Invoke(row);
    }

    internal static string OutcomeOf(Exception error) => error.GetType().Name;

    private static string? Shown(ItemId? item)
    {
        var value = item?.Value;
        if (string.IsNullOrEmpty(value)) { return null; }
        if (value.Contains('\\') || value.Contains(":/", StringComparison.Ordinal) || value.StartsWith('/')) { return "redacted"; }
        return value;
    }
}
