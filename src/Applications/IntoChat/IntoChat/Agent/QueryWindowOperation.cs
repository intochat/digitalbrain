using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;
using Orleans;
using Orleans.Runtime;
using Orleans.Metadata;

namespace IntoChat.Agent;

[GenerateSerializer, Alias("intochat.query-window-result")]
public sealed record QueryWindowResult(
    [property: Id(0)] string WindowId,
    [property: Id(1)] string TableId,
    [property: Id(2)] string Title,
    [property: Id(3)] long WorkspaceRevision)
{
    public bool RemoteManaged => true;
}

// A short durable journal; the cancellable database work runs outside its activation.
[Alias("intochat.query-window-operation"), DefaultGrainType("intochat.query-window-operation")]
public interface IQueryWindowOperation : IGrainWithStringKey
{
    Task<QueryWindowProgress> Begin(string fingerprint);
    Task TableCreated();
    Task Complete(QueryWindowResult result);
    Task Interrupt();
}

[GenerateSerializer, Alias("intochat.query-window-progress")]
public sealed record QueryWindowProgress
{
    [Id(0)] public string? Fingerprint { get; init; }
    [Id(1)] public string Stage { get; init; } = "pending";
    [Id(2)] public QueryWindowResult? Result { get; init; }
}

[GrainType("intochat.query-window-operation")]
internal sealed class QueryWindowOperationNeuron(
    [PersistentState("operation", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<QueryWindowProgress> store)
    : Grain, IQueryWindowOperation
{
    public async Task<QueryWindowProgress> Begin(string fingerprint)
    {
        if (store.State.Fingerprint is { } previous && previous != fingerprint)
            { throw new InvalidOperationException("This tool call already belongs to different input."); }
        if (store.State.Fingerprint is null)
            { await Save(store.State with { Fingerprint = fingerprint }); }
        return store.State;
    }
    public Task TableCreated() => store.State.Result is null ? Save(store.State with { Stage = "table-created" }) : Task.CompletedTask;
    public Task Complete(QueryWindowResult result) => store.State.Result is null ? Save(store.State with { Stage = "complete", Result = result }) : Task.CompletedTask;
    public Task Interrupt() => store.State.Result is null ? Save(store.State with { Stage = "interrupted" }) : Task.CompletedTask;
    private async Task Save(QueryWindowProgress next)
    {
        var previous = store.State;
        store.State = next;
        try { await store.WriteStateAsync(); }
        catch { store.State = previous; throw; }
    }
}

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
