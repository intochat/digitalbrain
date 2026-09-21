using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;

namespace IntoChat.Workspace.Queries;

internal sealed class QueryWindowOperation(IDigitalBrain brain, Func<Task>? afterTableCreated = null)
{
    public async Task<QueryWindowResult> ExecuteAsync(string scopeId, string runId, string callId, string title, string sql, CancellationToken ct)
    {
        foreach (var identity in new[] { scopeId, runId, callId })
        {
            if (string.IsNullOrWhiteSpace(identity) || identity.Length > 256)
            { throw new ArgumentException("A bounded scope, run and tool call identity is required."); }
        }
        var identityHash = Hash(JsonSerializer.Serialize(new[] { scopeId, runId, callId }));
        var operationId = "query-" + identityHash;
        var tableId = "table-" + identityHash;
        var journal = brain.Get<IQueryWindowOperation>(operationId);
        var progress = await journal.Begin(Hash(JsonSerializer.Serialize(new[] { title, sql }))).WaitAsync(ct);
        if (progress.Result is { } done) { return done; }
        try
        {
            ct.ThrowIfCancellationRequested();
            var table = brain.Get<ISupabaseTable>(tableId);
            await table.CreateFromQueryOnce(operationId, new(title, sql), ct).WaitAsync(ct);
            await journal.TableCreated().WaitAsync(ct);
            if (afterTableCreated is not null) { await afterTableCreated().WaitAsync(ct); }
            var workspace = brain.Get<IWorkspace>(scopeId);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var state = await workspace.Read().WaitAsync(ct);
                try
                {
                    state = await workspace.Open(new(operationId, tableId, title, new(tableId), state.Revision)).WaitAsync(ct);
                    var result = new QueryWindowResult(tableId, tableId, title, state.Revision);
                    await journal.Complete(result).WaitAsync(ct);
                    return result;
                }
                catch (WorkspaceRevisionConflictException) when (attempt < 7) { }
            }
            throw new InvalidOperationException("Workspace changed too often; retry this operation.");
        }
        catch (OperationCanceledException)
        {
            await journal.Interrupt();
            throw;
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
