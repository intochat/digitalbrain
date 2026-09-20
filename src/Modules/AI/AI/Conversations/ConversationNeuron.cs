using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.AI.Conversations;

[GenerateSerializer, Alias("ai.conversation-storage")]
internal sealed record ConversationStorage
{
    [Id(0)] public long Revision { get; init; }
    [Id(1)] public string? ActiveRun { get; init; }
    [Id(2)] public string? RuntimeId { get; init; }
    [Id(3)] public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.Ordinal);
    [Id(4)] public List<ConversationTurn> Turns { get; init; } = [];
}

[GrainType("ai.conversation")]
internal sealed class ConversationNeuron(
    [PersistentState("conversation", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ConversationStorage> store)
    : Neuron, IConversation
{
    public async Task<ConversationState> Begin(string runId, string message, long expectedRevision, string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.Length > 200 || string.IsNullOrWhiteSpace(message) || message.Length > 32000 || string.IsNullOrWhiteSpace(runtimeId))
            { throw new ArgumentException("Provide a run identity and a message of at most 32000 characters."); }
        var current = store.State;
        if (current.Inputs.TryGetValue(runId, out var previous))
        {
            if (previous != message) { throw new InvalidOperationException("Run ID already belongs to another message."); }
            if (current.Turns.Any(t => t.RunId == runId) || current.ActiveRun == runId && current.RuntimeId == runtimeId) { return Snapshot(); }
        }
        if (current.Revision != expectedRevision) { throw new InvalidOperationException("Conversation revision conflict."); }
        if (current.ActiveRun is not null && current.RuntimeId == runtimeId) { throw new InvalidOperationException("A conversation run is already active."); }
        var inputs = new Dictionary<string, string>(current.Inputs, StringComparer.Ordinal) { [runId] = message };
        await Save(current with { Revision = current.Revision + 1, ActiveRun = runId, RuntimeId = runtimeId, Inputs = inputs });
        return Snapshot();
    }
    public async Task<ConversationState> Complete(ConversationTurn turn)
    {
        var current = store.State;
        if (current.ActiveRun != turn.RunId || !current.Inputs.TryGetValue(turn.RunId, out var input) || input != turn.UserText)
            { throw new InvalidOperationException("This run is no longer active."); }
        await Save(current with { Revision = current.Revision + 1, ActiveRun = null, Turns = [..current.Turns, turn with { ResultIds = turn.ResultIds.ToArray() }] });
        return Snapshot();
    }
    public async Task<ConversationState> Interrupt(string runId)
    {
        if (store.State.ActiveRun == runId) { await Save(store.State with { Revision = store.State.Revision + 1, ActiveRun = null }); }
        return Snapshot();
    }
    public Task<ConversationState> Read() => Task.FromResult(Snapshot());
    private ConversationState Snapshot() => new(store.State.Revision, store.State.ActiveRun, store.State.Turns.ToArray());
    private async Task Save(ConversationStorage next)
    {
        var previous = store.State;
        store.State = next;
        try { await store.WriteStateAsync(); }
        catch { store.State = previous; throw; }
    }
}
