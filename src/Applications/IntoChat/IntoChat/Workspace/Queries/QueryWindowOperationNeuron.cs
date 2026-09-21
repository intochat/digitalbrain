using DigitalBrain.Contracts;
using Orleans;
using Orleans.Runtime;

namespace IntoChat.Workspace.Queries;

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