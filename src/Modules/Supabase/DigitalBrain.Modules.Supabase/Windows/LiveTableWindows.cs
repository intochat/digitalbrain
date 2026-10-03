using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using Orleans;

namespace DigitalBrain.Supabase.Windows;

// The live-table window the assistant works in: open creates the table and its workspace window
// once per tool call, and refine mutates that same window. Replay safety lives on
// CreateFromQueryOnce's operation id and the workspace Open receipt; the deleted query-window
// journal added nothing to that.
public sealed class LiveTableWindows(IDigitalBrain brain)
{
    private const int MaxRefineRetries = 8;

    public async Task<QueryWindowResult> OpenAsync(string scopeId, string runId, string callId, string title, string sql, CancellationToken ct, string source = "supabase")
    {
        foreach (var identity in new[] { scopeId, runId, callId })
        {
            if (string.IsNullOrWhiteSpace(identity) || identity.Length > 256)
            { throw new ArgumentException("A bounded scope, run and tool call identity is required."); }
        }
        var identityHash = Hash(JsonSerializer.Serialize<string[]>(source == "supabase" ? [scopeId, runId, callId] : [scopeId, runId, callId, source]));
        var operationId = "query-" + identityHash;
        var tableId = "table-" + identityHash;
        ct.ThrowIfCancellationRequested();
        await brain.Get<ISupabaseTable>(tableId).CreateFromQueryOnce(operationId, new(title, sql) { Source = source }, ct).WaitAsync(ct);
        var receipt = await WorkspaceWindows.RetryOnConflictAsync(0, revision =>
            brain.Get<IWorkspace>(scopeId).Open(new(operationId, tableId, title, WindowReference.Table(tableId), revision)), ct);
        return new QueryWindowResult(tableId, tableId, title, receipt.AppliedRevision) { Source = source };
    }

    public async Task<SupabaseTableSnapshot> RefineAsync(string scopeId, string tableId, IReadOnlyList<SupabaseTableFilter> filters,
        SupabaseTableSort? sort, IReadOnlyList<string>? visibleColumns, CancellationToken ct, string? expectedSource = null)
    {
        var table = await ResolveAsync(scopeId, tableId, ct, expectedSource);
        var snapshot = await table.Read(new(0, 1)).WaitAsync(ct) ?? throw new SupabaseTableNotFoundException(tableId);
        var visible = visibleColumns is { Count: > 0 } ? visibleColumns : snapshot.VisibleColumns;
        var revision = snapshot.Revision;
        for (var attempt = 0; attempt < MaxRefineRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await table.UpdateView(new(revision, filters, sort, visible)).WaitAsync(ct);
            }
            catch (SupabaseTableRevisionConflictException conflict) when (attempt < MaxRefineRetries - 1)
            {
                revision = conflict.CurrentRevision;
            }
        }
        throw new InvalidOperationException("The table changed too often; retry this refinement.");
    }

    // Reads schema, counts and (Public-only) row values from the saved view the workspace holds.
    public async Task<object> ReadAsync(string scopeId, string tableId, string? aggregate, string? aggregateColumn,
        IReadOnlyList<string>? columns, int? limit, CancellationToken ct, string? expectedSource = null)
    {
        var table = await ResolveAsync(scopeId, tableId, ct, expectedSource);
        var pageLimit = aggregate is null ? Math.Clamp(limit ?? 25, 1, 200) : 1;
        var snapshot = await table.Read(new(0, pageLimit)).WaitAsync(ct) ?? throw new SupabaseTableNotFoundException(tableId);
        var readable = SupabaseTableReadPolicy.PublicColumns(snapshot);
        var projected = columns is { Count: > 0 }
            ? readable.Where(column => columns.Contains(column.Id, StringComparer.Ordinal)).ToArray()
            : readable;
        if (columns is { Count: > 0 } && projected.Count == 0)
        { throw new SupabaseTableValidationException("No readable Public columns were requested; Personal and Credential values are never returned."); }
        var positions = snapshot.Columns.Select((column, index) => (column.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        var rows = aggregate is null
            ? snapshot.Rows.Select(row => projected.Select(column => row.Cells[positions[column.Id]]).ToArray()).ToArray()
            : [];
        SupabaseTableAggregate? computed = null;
        if (aggregate is not null)
        {
            computed = await table.Aggregate(new(aggregateColumn ?? string.Empty, aggregate.ToLowerInvariant())).WaitAsync(ct);
        }
        return new
        {
            windowId = tableId,
            revision = snapshot.Revision,
            title = snapshot.Title,
            source = snapshot.Source,
            schema = snapshot.Columns.Select(column => new
            {
                id = column.Id,
                kind = SupabaseTableReadPolicy.KindOf(column).ToString(),
                sensitivity = SupabaseTableReadPolicy.SensitivityOf(column).ToString(),
                readable = SupabaseTableReadPolicy.IsPublic(column),
            }).ToArray(),
            totalRows = snapshot.TotalRows,
            filteredRows = snapshot.FilteredRows,
            columns = projected.Select(column => column.Id).ToArray(),
            rowsRead = rows.Length,
            aggregate = computed,
            rows,
        };
    }

    private async Task<ISupabaseTable> ResolveAsync(string scopeId, string tableId, CancellationToken ct, string? expectedSource)
    {
        var state = await brain.Get<IWorkspace>(scopeId).Read().WaitAsync(ct);
        if (!state.Windows.Any(window => window.Reference.Kind == WindowReference.TableKind && window.Reference.NeuronId == tableId))
        { throw new SupabaseTableNotFoundException(tableId); }
        var table = brain.Get<ISupabaseTable>(tableId);
        if (expectedSource is not null)
        {
            var summary = await table.ReadSummary().WaitAsync(ct) ?? throw new SupabaseTableNotFoundException(tableId);
            if (summary.Source != expectedSource)
            { throw new SupabaseTableValidationException($"This window uses {summary.Source}, but the request specifies {expectedSource}. Open a table from the requested database."); }
        }
        return table;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

// The handle the assistant gets for a window it opened: the window id only, never the rows.
[GenerateSerializer, Alias("intochat.query-window-result")]
public sealed record QueryWindowResult(
    [property: Id(0)] string WindowId,
    [property: Id(1)] string TableId,
    [property: Id(2)] string Title,
    [property: Id(3)] long WorkspaceRevision)
{
    [Id(4)] public string Source { get; init; } = "supabase";
    public bool RemoteManaged => true;
}
