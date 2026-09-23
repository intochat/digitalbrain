using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using DigitalBrain.AI.Agents.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI.Agents;

[GenerateSerializer, Alias("ai.agent-storage-envelope")]
internal sealed record AgentStorageEnvelope
{
    [Id(0)] public string Json { get; init; } = "";
}

[GenerateSerializer, Alias("ai.agent-storage")]
internal sealed record AgentStorage
{
    [Id(0)] public long Revision { get; init; }
    [Id(1)] public AgentDefinition Definition { get; init; } = new();
    [Id(2)] public List<AiMessage> History { get; init; } = [];
    [Id(3)] public AgentRunState? LastRun { get; init; }
    [Id(4)] public List<AgentEvent> Events { get; init; } = [];
    [Id(5)] public long EventSequence { get; init; }
    [Id(6)] public List<AgentConversationTurn> Turns { get; init; } = [];
    [Id(7)] public string? ActiveRun { get; init; }
    [Id(8)] public string? HistorySummary { get; init; }
    [Id(9)] public Dictionary<string, string> RunInputs { get; init; } = new(StringComparer.Ordinal);
    [Id(10)] public string? ConversationSummary { get; init; }
}

[GrainType("agent")]
internal sealed class AgentNeuron(
    [PersistentState("agent", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AgentStorageEnvelope> store,
    IAgentTurnRunner runner) : Neuron, IAgent
{
    private CancellationTokenSource? _active;
    private bool _mutating;
    private AgentStorage _state = new();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _state = string.IsNullOrEmpty(store.State.Json) ? new() : JsonSerializer.Deserialize<AgentStorage>(store.State.Json)!;
        if (_state.LastRun is { Status: AgentRunStatus.Running } interrupted)
        {
            await Save(_state with
            {
                LastRun = interrupted with
                {
                    Status = AgentRunStatus.Interrupted,
                    EndedAt = DateTimeOffset.UtcNow,
                    Error = "Activation ended before the run committed; external tool outcomes may be unknown."
                }
            });
        }
        await base.OnActivateAsync(cancellationToken);
    }

    public async Task Configure(AgentDefinition definition, long expectedRevision, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(definition);
        if (_state.Revision != expectedRevision) { throw new InvalidOperationException("Agent revision conflict."); }
        if (definition.MaxModelCalls is < 1 or > 128 || definition.Timeout <= TimeSpan.Zero || definition.Timeout > TimeSpan.FromMinutes(10)
            || definition.MaxHistoryMessages is < 2 or > 4096) { throw new ArgumentException("Invalid agent execution limits.", nameof(definition)); }
        if (definition.Instructions.Length > 32000 || definition.Tools.Count > 128
            || definition.Tools.Any(string.IsNullOrWhiteSpace) || definition.Tools.Distinct(StringComparer.Ordinal).Count() != definition.Tools.Count)
        { throw new ArgumentException("Instructions or tool selection are invalid.", nameof(definition)); }
        _mutating = true;
        try
        {
            await Save(_state with
            {
                Revision = _state.Revision + 1,
                Definition = definition with
                { Tools = definition.Tools.ToArray(), Capabilities = definition.Capabilities.ToArray(), RoutingExamples = definition.RoutingExamples.ToArray() }
            });
            await Notify(new AgentConfigured(this.GetPrimaryKeyString(), _state.Revision));
        }
        finally { _mutating = false; }
    }

    public async Task<AgentReply> Ask(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await Respond(User(request.Message), request.Model, request.System, CancellationToken.None);
        return new(response.Text);
    }
    public async Task<string> GetResponse(string prompt, CancellationToken ct = default) => (await GetRichResponse(prompt, ct)).Text;
    public Task<AgentResponse> GetRichResponse(string prompt, CancellationToken ct = default) => GetRichResponse(User(prompt), ct);
    public Task<AgentResponse> GetRichResponse(AiMessage message, CancellationToken ct = default) => Respond(message, null, null, ct);
    public IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct = default) => GetResponseStream(User(prompt), ct);
    public async IAsyncEnumerable<string> GetResponseStream(AiMessage message, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in Execute(message, null, null, streaming: true, ct))
        { if (update.Text is { } text) { yield return text; } }
    }
    private async Task<AgentResponse> Respond(AiMessage message, AgentModelSelection? model, string? instructions, CancellationToken ct)
    {
        AgentResponse? result = null;
        await foreach (var update in Execute(message, model, instructions, streaming: false, ct)) { result = update.Response ?? result; }
        return result ?? throw new InvalidOperationException("The run produced no result.");
    }

    private async IAsyncEnumerable<RunUpdate> Execute(AiMessage message, AgentModelSelection? model, string? instructions,
        bool streaming, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(message);
        if (message.Role != "user" || message.Content.Count == 0) { throw new ArgumentException("An agent accepts a non-empty user message.", nameof(message)); }
        if (message.Content.Any(c => c is not (AiText or AiImage or AiAudio))
            || message.Content.OfType<AiText>().Sum(t => (long)t.Text.Length) > 32000)
        { throw new ArgumentException("User messages may contain bounded text, images or audio, never tool calls or reasoning.", nameof(message)); }
        _ = InferenceMapping.ToChatMessage(message);
        var definition = _state.Definition;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(definition.Timeout);
        _active = lifetime;
        var run = new AgentRunState(Guid.NewGuid().ToString("N"), AgentRunStatus.Running, _state.Revision, DateTimeOffset.UtcNow,
            Definition: definition with { Model = model ?? definition.Model, Instructions = instructions ?? definition.Instructions });
        var completed = false;
        var started = false;
        var cancelled = false;
        var suspended = false;
        string? error = null;
        try
        {
            await Save(_state with { LastRun = run });
            started = true;
            await Record(run.RunId, "started");
            await Notify(new AgentRunChanged(this.GetPrimaryKeyString(), run));
            var text = new StringBuilder();
            AgentTurnEvent.Completed? output = null;
            var bounded = BoundHistory(_state.History, Math.Max(1, definition.MaxHistoryMessages - 1), _state.HistorySummary);
            var request = new AgentTurnRequest(this.GetPrimaryKeyString(), run.RunId, this.GetPrimaryKeyString(), [], "",
                model ?? definition.Model, AppendSummary(instructions ?? definition.Instructions, bounded.Summary), definition.Tools,
                bounded.Messages, message, streaming, definition.MaxModelCalls, definition.Timeout, definition.Options);
            await using var iterator = runner.RunAsync(request, lifetime.Token).GetAsyncEnumerator(lifetime.Token);
            long sequence = 0;
            while (true)
            {
                bool moved;
                try { moved = await iterator.MoveNextAsync(); }
                catch (Exception exception)
                {
                    cancelled = exception is OperationCanceledException;
                    error = cancelled ? "The run was cancelled." : exception.Message;
                    throw;
                }
                if (!moved) { break; }
                switch (iterator.Current)
                {
                    case AgentTurnEvent.ModelSelected selected:
                        run = run with { Model = selected.Model, Capabilities = selected.Descriptor };
                        await Save(_state with { LastRun = run });
                        break;
                    case AgentTurnEvent.Text delta:
                        text.Append(delta.Content);
                        await Notify(new AgentOutputDelta(this.GetPrimaryKeyString(), run.RunId, ++sequence, delta.Content));
                        suspended = true;
                        yield return new(delta.Content, null);
                        suspended = false;
                        break;
                    case AgentTurnEvent.Completed result: output = result; break;
                    case AgentTurnEvent.ToolStarted tool: await Record(run.RunId, "tool-started", tool.CallId, tool.Name); break;
                    case AgentTurnEvent.ToolCompleted tool: await Record(run.RunId, "tool-completed", tool.CallId, tool.Name); break;
                    case AgentTurnEvent.ToolFailed tool: await Record(run.RunId, "tool-failed", tool.CallId, tool.Name); break;
                    case AgentTurnEvent.Failed failed:
                        error = failed.Message;
                        cancelled = failed.Cancelled;
                        if (cancelled) { throw new OperationCanceledException(error, lifetime.Token); }
                        throw new InvalidOperationException(error);
                }
            }
            lifetime.Token.ThrowIfCancellationRequested();
            if (output is null) { error = "The run ended without a completed response."; throw new InvalidOperationException(error); }
            var response = new AgentResponse(run.RunId, text.ToString(), output.Messages, output.Usage);
            run = run with { Status = AgentRunStatus.Completed, EndedAt = DateTimeOffset.UtcNow, Response = response };
            var terminal = new AgentEvent(_state.EventSequence + 1, run.RunId, "completed", run.EndedAt.Value);
            var history = BoundHistory([.. _state.History, message, .. output.Messages], definition.MaxHistoryMessages, _state.HistorySummary);
            await Save(_state with
            {
                History = history.Messages,
                HistorySummary = history.Summary,
                LastRun = run,
                EventSequence = terminal.Sequence,
                Events = [.. _state.Events.TakeLast(255), terminal]
            });
            completed = true;
            // Terminal state and history are already committed; notifications cannot undo them.
            await Notify(new AgentRunChanged(this.GetPrimaryKeyString(), run));
            await Notify(new AgentReplied(this.GetPrimaryKeyString(), response.Text, run.EndedAt.Value));
            yield return new(null, response);
        }
        finally
        {
            try
            {
                if (started && !completed)
                {
                    run = run with
                    {
                        Status = cancelled || lifetime.IsCancellationRequested || suspended ? AgentRunStatus.Cancelled : AgentRunStatus.Failed,
                        EndedAt = DateTimeOffset.UtcNow,
                        Error = error ?? (suspended ? "The response stream was abandoned." : "Execution failed before committing its result.")
                    };
                    await Save(_state with { LastRun = run });
                    await Record(run.RunId, run.Status.ToString().ToLowerInvariant());
                    await Notify(new AgentRunChanged(this.GetPrimaryKeyString(), run));
                }
            }
            finally { _active = null; }
        }
    }

    public Task<IReadOnlyList<AiMessage>> GetHistory(CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<AiMessage>>(_state.History.ToArray()); }
    public async Task ClearHistory(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); EnsureIdle(); _mutating = true;
        try
        {
            await Save(_state with { History = [], HistorySummary = null, Revision = _state.Revision + 1 });
            await Notify(new AgentHistoryCleared(this.GetPrimaryKeyString(), _state.Revision));
        }
        finally { _mutating = false; }
    }

    public Task<AgentConversationState> ReadConversation(CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(Snapshot()); }

    public async Task<AgentConversationState> BeginConversation(AgentConversationRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RunId) || request.RunId.Length > 200)
        { throw new ArgumentException("A conversation run needs an identity of at most 200 characters.", nameof(request)); }
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 32000)
        { throw new ArgumentException("A conversation run needs a message of at most 32000 characters.", nameof(request)); }
        var existing = _state.Turns.FirstOrDefault(turn => turn.RunId == request.RunId);
        if (existing is not null)
        {
            // A retained turn can be replayed only for the exact same request. Reusing a run id
            // with a different payload must never return an unrelated stored answer.
            if (existing.UserText != request.Message) { throw new InvalidOperationException("Run ID already belongs to another message."); }
            return Snapshot();
        }
        if (_state.RunInputs.TryGetValue(request.RunId, out var previous) && previous != request.Message)
        { throw new InvalidOperationException("Run ID already belongs to another message."); }
        if (_state.ActiveRun is not null) { throw new InvalidOperationException("A conversation run is already active."); }
        _mutating = true;
        try
        {
            var inputs = new Dictionary<string, string>(_state.RunInputs, StringComparer.Ordinal) { [request.RunId] = request.Message };
            await Save(_state with { Revision = _state.Revision + 1, ActiveRun = request.RunId, RunInputs = inputs });
            return Snapshot();
        }
        finally { _mutating = false; }
    }

    public async Task<AgentConversationState> CompleteConversation(AgentConversationTurn turn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(turn);
        if (_state.ActiveRun != turn.RunId || !_state.RunInputs.TryGetValue(turn.RunId, out var input) || input != turn.UserText)
        { throw new InvalidOperationException("This run is no longer active."); }
        _mutating = true;
        try
        {
            var turns = new List<AgentConversationTurn>(_state.Turns) { turn with { ResultIds = turn.ResultIds.ToArray() } };
            var bounded = BoundTurns(turns, _state.Definition.MaxHistoryMessages, _state.ConversationSummary);
            await Save(_state with
            {
                Revision = _state.Revision + 1,
                ActiveRun = null,
                Turns = bounded.Turns,
                ConversationSummary = bounded.Summary,
                RunInputs = WithoutRun(_state.RunInputs, turn.RunId)
            });
            return Snapshot();
        }
        finally { _mutating = false; }
    }

    public async Task<AgentConversationState> InterruptConversation(string runId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureIdle();
        if (_state.ActiveRun != runId) { return Snapshot(); }
        _mutating = true;
        try
        {
            await Save(_state with { Revision = _state.Revision + 1, ActiveRun = null, RunInputs = WithoutRun(_state.RunInputs, runId) });
            return Snapshot();
        }
        finally { _mutating = false; }
    }

    // Committed or interrupted run inputs are redundant once the turn carries its own user text;
    // keeping them would grow the run-input map without bound and outlive turn eviction.
    private static Dictionary<string, string> WithoutRun(Dictionary<string, string> inputs, string runId)
    {
        if (!inputs.ContainsKey(runId)) { return inputs; }
        var next = new Dictionary<string, string>(inputs, StringComparer.Ordinal);
        next.Remove(runId);
        return next;
    }

    private AgentConversationState Snapshot() => new(_state.Revision, _state.ActiveRun, _state.Turns.ToArray(), _state.ConversationSummary);
    public Task<AgentState> GetState(CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(new AgentState(_state.Revision, _state.Definition, _state.LastRun, _state.History.Count)); }
    public Task<AgentMetadata> GetMetadata(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var definition = _state.Definition;
        return Task.FromResult(new AgentMetadata(this.GetPrimaryKeyString(), definition.DisplayName, definition.Description, definition.Capabilities, definition.RoutingExamples));
    }
    public Task<AgentUsage?> GetLastUsage(CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_state.LastRun?.Response?.Usage); }
    public Task<ModelDescriptor> GetCapabilities(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ServiceProvider.GetRequiredService<InferenceService>().Describe(_state.Definition.Model));
    }
    public Task<IReadOnlyList<AgentEvent>> GetEventLog(CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<AgentEvent>>(_state.Events.ToArray()); }
    public Task Cancel(CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); _active?.Cancel(); return Task.CompletedTask; }

    private async Task Record(string runId, string kind, string? callId = null, string? tool = null)
    {
        var item = new AgentEvent(_state.EventSequence + 1, runId, kind, DateTimeOffset.UtcNow, callId, tool);
        await Save(_state with { EventSequence = item.Sequence, Events = [.. _state.Events.TakeLast(255), item] });
        if (tool is not null) { await Notify(new AgentToolCallChanged(this.GetPrimaryKeyString(), item)); }
    }
    private void EnsureIdle()
    { if (_active is not null || _mutating) { throw new InvalidOperationException("This agent already has an active operation."); } }
    private static AiMessage User(string prompt)
    { ArgumentException.ThrowIfNullOrWhiteSpace(prompt); if (prompt.Length > 32000) { throw new ArgumentException("Prompt exceeds 32000 characters.", nameof(prompt)); } return new("user", [new AiText(prompt)]); }
    private async Task Save(AgentStorage next)
    {
        var previous = store.State;
        store.State = new() { Json = JsonSerializer.Serialize(next) };
        try { await store.WriteStateAsync(); _state = next; }
        catch { store.State = previous; throw; }
    }
    private async Task Notify(Signal signal)
    {
        try { await PublishAsync(signal); }
        catch (Exception exception)
        {
            ServiceProvider.GetRequiredService<ILogger<AgentNeuron>>().LogWarning(exception,
                "Agent signal delivery failed; consumers can reconcile through GetState and GetHistory.");
        }
    }
    private sealed record RunUpdate(string? Text, AgentResponse? Response);

    // Both the message history and the conversation turns stay inside the configured budget.
    // Overflow folds the oldest content into a bounded summary instead of failing the run.
    private static (List<AiMessage> Messages, string? Summary) BoundHistory(
        IReadOnlyList<AiMessage> messages, int maxMessages, string? summary)
    {
        if (maxMessages < 1) { maxMessages = 1; }
        if (messages.Count <= maxMessages) { return ([.. messages], summary); }
        var drop = messages.Count - maxMessages;
        var excerpts = messages.Take(drop)
            .Select(message => string.Concat(message.Content.OfType<AiText>().Select(text => text.Text)));
        return ([.. messages.Skip(drop)], MergeSummary(summary, excerpts));
    }

    private static (List<AgentConversationTurn> Turns, string? Summary) BoundTurns(
        IReadOnlyList<AgentConversationTurn> turns, int maxMessages, string? summary)
    {
        var maxTurns = Math.Max(1, maxMessages / 2);
        if (turns.Count <= maxTurns) { return ([.. turns], summary); }
        var drop = turns.Count - maxTurns;
        return ([.. turns.Skip(drop)], MergeSummary(summary, turns.Take(drop).Select(turn => turn.UserText)));
    }

    private static string? MergeSummary(string? summary, IEnumerable<string> dropped)
    {
        var added = string.Join(" ", dropped
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Length <= 120 ? text : text[..120]));
        if (added.Length == 0) { return summary; }
        var combined = string.IsNullOrEmpty(summary) ? added : summary + " " + added;
        return combined.Length <= 4000 ? combined : combined[^4000..];
    }

    private static string AppendSummary(string instructions, string? summary) =>
        string.IsNullOrEmpty(summary) ? instructions : instructions + "\nEarlier conversation summary: " + summary;
}