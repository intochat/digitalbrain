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
        if (_state.History.Count + 2 > definition.MaxHistoryMessages)
        { throw new InvalidOperationException("Conversation history budget reached. Clear history or raise the configured limit."); }
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
            var request = new AgentTurnRequest(this.GetPrimaryKeyString(), run.RunId, this.GetPrimaryKeyString(), [], "",
                model ?? definition.Model, instructions ?? definition.Instructions, definition.Tools,
                _state.History, message, streaming, definition.MaxModelCalls, definition.Timeout, definition.Options);
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
            if (_state.History.Count + 1 + output.Messages.Count > definition.MaxHistoryMessages)
            { error = "The response exceeds the conversation history budget."; throw new InvalidOperationException(error); }
            var response = new AgentResponse(run.RunId, text.ToString(), output.Messages, output.Usage);
            run = run with { Status = AgentRunStatus.Completed, EndedAt = DateTimeOffset.UtcNow, Response = response };
            var terminal = new AgentEvent(_state.EventSequence + 1, run.RunId, "completed", run.EndedAt.Value);
            await Save(_state with
            {
                History = [.. _state.History, message, .. output.Messages],
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
            await Save(_state with { History = [], Revision = _state.Revision + 1 });
            await Notify(new AgentHistoryCleared(this.GetPrimaryKeyString(), _state.Revision));
        }
        finally { _mutating = false; }
    }
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
}