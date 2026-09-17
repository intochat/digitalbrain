using System.Globalization;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using McpOperations = DigitalBrain.Mcp.BrainOperations;

namespace DigitalBrain.AI;

// A Session neuron with a model attached. Instruct is its configuration, held as
// latest-per-type; Ask is answered with Reply and Turn with Said,
// both on the incoming correlation. Everything else is journaled and ignored.
[GrainType(AIVocabulary.AgentType)]
internal sealed class AgentNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<AgentState>> state)
    : Neuron<AgentState>(runtime, state), IAgent, IAgentLifecycle
{
    // Descriptors are fixed for the life of the silo, so the typed functions an Instruct.tools
    // entry generates are built once per activation. Neuron turns are serialized: no locking.
    private readonly Dictionary<string, AIFunction[]> _typedFunctions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _takenFunctionNames = new(StringComparer.Ordinal);

    public Task<AgentSnapshot> GetState() => Task.FromResult(Snapshot(Managed()));

    public Task<AgentResponse?> GetResponse(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return Task.FromResult(Managed().Tasks.FirstOrDefault(task => task.Snapshot.TaskId == requestId)?.Snapshot);
    }

    public Task<IReadOnlyList<AgentMessage>> GetHistory()
    {
        var history = JsonSerializer.Deserialize<List<ChatMessage>>(Managed().HistoryJson, AgentLifecycle.Json) ?? [];
        return Task.FromResult<IReadOnlyList<AgentMessage>>(
            history.Where(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
                .Where(message => !string.IsNullOrEmpty(message.Text))
                .Select(message => new AgentMessage(message.Role.ToString(), message.Text)).ToArray());
    }

    public Task ClearHistory()
    {
        var current = Managed();
        if (current.Tasks.Any(task => task.Snapshot.Status is "Queued" or "Running"))
        {
            throw new InvalidOperationException("Cancel or finish outstanding requests before clearing conversation history.");
        }
        return SaveManagedAsync(current with { HistoryJson = "[]" });
    }

    async Task IAgentLifecycle.Initialize(AgentInitialization initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        if (State?.InitializationStopped == true) { throw new InvalidOperationException("This agent's owner scope is closed."); }
        if (initialization.Snapshot.AgentId != Id) { throw new ArgumentException("The agent initialization address does not match its identity.", nameof(initialization)); }
        if (State?.Managed is { } existing)
        {
            if (existing.RequestHash != initialization.RequestHash) { throw new InvalidOperationException("This agent was initialized with a different specification."); }
            await QueuePendingAsync(existing).ConfigureAwait(true);
            return;
        }
        if (State?.Sessions.Count > 0) { throw new InvalidOperationException("An existing legacy agent cannot be overwritten by the builder."); }

        var current = new AgentManagedState(initialization.Snapshot, initialization.RequestHash, "[]", [], []);
        if (initialization.InitialMessage is { } message)
        {
            current = current with { Tasks = [NewTask(initialization.Snapshot.InitialTaskId!, message)] };
        }
        await SaveAsync(new AgentState([], current)).ConfigureAwait(true);
        await QueuePendingAsync(current).ConfigureAwait(true);
    }

    async Task IAgentLifecycle.Retire(CancelAgent command)
    {
        if (State?.Managed is null)
        {
            await SaveAsync((State ?? new AgentState([])) with { InitializationStopped = true }).ConfigureAwait(true);
            return;
        }
        await Cancel(command).ConfigureAwait(true);
    }

    Task<AgentWork?> IAgentLifecycle.Work(string requestId)
    {
        var current = State?.Managed;
        var task = current?.Tasks.FirstOrDefault(item => item.Snapshot.TaskId == requestId);
        return Task.FromResult(current is not null && current.Snapshot.Status != "Stopped" && task?.Snapshot.Status == "Running"
            ? new AgentWork(current.Snapshot with { Tasks = [] }, task.Snapshot, current.HistoryJson) : null);
    }

    public async Task<AgentResponse> Submit(AgentRequest command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value == Guid.Empty) { throw new ArgumentException("A request requires a nonempty command id.", nameof(command)); }
        AgentLifecycle.RequireText(command.Message, nameof(command.Message), 8_000);
        var taskId = command.TaskId ?? command.Id.ToString();
        AgentLifecycle.RequireText(taskId, nameof(command.TaskId), 128);
        var current = Managed();
        var requestHash = AgentLifecycle.Fingerprint(new { Method = "send", taskId, command.Message });
        CheckReceipt(current, command.Id, requestHash);
        var task = current.Tasks.FirstOrDefault(item => item.Snapshot.TaskId == taskId);
        if (task is not null && task.RequestHash != AgentLifecycle.Hash(command.Message))
        {
            throw new ArgumentException("This agent task id already identifies a different message.", nameof(command));
        }
        if (task is null)
        {
            if (current.Snapshot.Status == "Stopped") { throw new InvalidOperationException("This agent is stopped. Build a new agent to continue."); }
            if (current.Tasks.Count >= AgentLifecycle.MaxTasks) { throw new InvalidOperationException("This agent reached its 64-task lifetime limit. Build a new agent to continue."); }
            task = NewTask(taskId, command.Message);
            current = current with { Tasks = [.. current.Tasks, task] };
        }
        current = Receipt(current, command.Id, requestHash, taskId);
        await SaveManagedAsync(current).ConfigureAwait(true);
        if (task.Snapshot.Status == "Queued") { await QueueAsync(task).ConfigureAwait(true); }
        return task.Snapshot;
    }

    public async Task<AgentSnapshot> Cancel(CancelAgent command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value == Guid.Empty) { throw new ArgumentException("Cancellation requires a nonempty command id.", nameof(command)); }
        var current = Managed();
        var requestHash = AgentLifecycle.Fingerprint(new { Method = "stop", command.TaskId });
        CheckReceipt(current, command.Id, requestHash);
        if (command.TaskId is not null && !current.Tasks.Any(task => task.Snapshot.TaskId == command.TaskId))
        {
            throw new ArgumentException("The requested agent task does not exist.", nameof(command));
        }
        var affected = current.Tasks.Where(task => (command.TaskId is null || task.Snapshot.TaskId == command.TaskId)
            && task.Snapshot.Status is "Queued" or "Running").ToArray();
        var changed = affected.Length > 0 || command.TaskId is null && current.Snapshot.Status != "Stopped";
        current = current with
        {
            Snapshot = current.Snapshot with { Status = command.TaskId is null ? "Stopped" : current.Snapshot.Status },
            Tasks = current.Tasks.Select(task => affected.Contains(task) ? task with
            {
                Snapshot = task.Snapshot with { Status = "Cancelled", Error = "Cancelled by the owner.", CompletedAt = TimeProvider.GetUtcNow() },
            } : task).ToArray(),
        };
        if (changed)
        {
            // At most one first cancellation per lifetime task plus one whole-agent stop:
            // this reserved ledger is bounded by MaxTasks + 1 and never competes with sends.
            current = current with { StopReceipts = [.. current.StopReceipts ?? [], new(command.Id, requestHash, command.TaskId)] };
        }
        await SaveManagedAsync(current).ConfigureAwait(true);
        // Record cancellation before crossing to the worker. Late starts/completions consult this durable state.
        foreach (var task in current.Tasks.Where(task => (command.TaskId is null || task.Snapshot.TaskId == command.TaskId)
            && task.Snapshot.Status == "Cancelled" && task.WorkSignal is not null))
        {
            await GrainFactory.GetGrain<INeuron>(AgentLifecycle.Worker(Id, task.Snapshot.TaskId).ToGrainId())
                .CancelReaction(task.WorkSignal!.Value).ConfigureAwait(true);
        }
        if (current.Snapshot.Status != "Stopped" && current.Tasks.FirstOrDefault(static task => task.Snapshot.Status == "Queued") is { } next)
        {
            await QueueAsync(next with { PumpSignal = AgentLifecycle.Signal(Id + "/cancel-wake/" + command.Id) }).ConfigureAwait(true);
        }
        return Snapshot(current);
    }

    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        await base.OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);
        if (State?.Managed is { } current)
        {
            // The task ledger is written before queue admission, so this repairs a crash between those writes.
            await QueuePendingAsync(current).ConfigureAwait(true);
            if (current.Snapshot.Status != "Stopped" && !current.Tasks.Any(static task => task.Snapshot.Status == "Running")
                && current.Tasks.FirstOrDefault(static task => task.Snapshot.Status == "Queued") is { } next)
            {
                await QueueAsync(next with { PumpSignal = AgentLifecycle.Signal(Id + "/recovery/" + AgentLifecycle.Fingerprint(current.Tasks)) }).ConfigureAwait(true);
            }
            foreach (var task in current.Tasks.Where(static item => item.Snapshot.Status == "Cancelled" && item.WorkSignal is not null))
            {
                await GrainFactory.GetGrain<INeuron>(AgentLifecycle.Worker(Id, task.Snapshot.TaskId).ToGrainId())
                    .CancelReaction(task.WorkSignal!.Value).ConfigureAwait(true);
            }
        }
    }

    private async Task<bool> ReceiveManagedAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = State?.Managed;
        if (current is null)
        {
            if (State is not { InitializationStopped: true } stopped) { return false; }
            if (delivery.Signal.Type is AIVocabulary.Ask or AIVocabulary.Turn)
            {
                AnnounceReply(delivery, "This agent's owner scope is closed.");
                await SaveAsync(stopped, cancellationToken).ConfigureAwait(true);
            }
            return true;
        }
        if (delivery.Signal.Type == AgentLifecycle.Pump)
        {
            if (delivery.Source != Id) { return true; }
        }
        else if (delivery.Signal.Type == AgentLifecycle.Completed)
        {
            var result = JsonSerializer.Deserialize<AgentTaskResult>(delivery.Signal.Body, AgentLifecycle.Json)
                ?? throw new ArgumentException("Agent task completion is empty.");
            var task = current.Tasks.FirstOrDefault(item => item.Snapshot.TaskId == result.TaskId);
            if (task?.Snapshot.Status != "Running" || delivery.Source != AgentLifecycle.Worker(Id, result.TaskId)) { return true; }
            var completed = task with { Snapshot = task.Snapshot with
            {
                Status = result.Status, Output = result.Output, Error = result.Error, CompletedAt = TimeProvider.GetUtcNow(),
            } };
            current = current with
            {
                Tasks = current.Tasks.Select(item => item.Snapshot.TaskId == result.TaskId ? completed : item).ToArray(),
                HistoryJson = result.Status == "Completed" && result.HistoryJson is not null ? result.HistoryJson : current.HistoryJson,
            };
            if (task.ReplyTo is { } reply)
            {
                var text = result.Output ?? result.Error ?? string.Empty;
                Announce(task.Say ? Signal.FromJson(AIVocabulary.Said, new SaidBody(Id.ToString(), text), AIJson.Default.SaidBody)
                    : Signal.FromJson(AIVocabulary.Reply, new TextBody(text), AIJson.Default.TextBody), reply, task.Correlation);
            }
        }
        else if (delivery.Signal.Type is AIVocabulary.Ask or AIVocabulary.Turn)
        {
            if (current.Snapshot.Status == "Stopped")
            {
                AnnounceReply(delivery, "This agent is stopped.");
                await SaveManagedAsync(current, cancellationToken).ConfigureAwait(true);
                return true;
            }
            if (current.Tasks.Count >= AgentLifecycle.MaxTasks)
            {
                AnnounceReply(delivery, "This agent reached its task limit. Build a new agent to continue.");
                await SaveManagedAsync(current, cancellationToken).ConfigureAwait(true);
                return true;
            }
            var text = delivery.Signal.Type == AIVocabulary.Turn ? await TurnContextAsync(delivery).ConfigureAwait(true) : Bodies.Text(delivery.Signal.Body);
            if (System.Text.Encoding.UTF8.GetByteCount(text) > 8_000) { text = text[..Math.Min(text.Length, 2_000)]; }
            var task = NewTask(delivery.SignalId.ToString(), text) with
            {
                ReplyTo = delivery.Source, Correlation = delivery.CorrelationId, Say = delivery.Signal.Type == AIVocabulary.Turn,
            };
            current = current with { Tasks = [.. current.Tasks, task] };
        }
        else { return true; }

        current = StartNext(current);
        await SaveManagedAsync(current, cancellationToken).ConfigureAwait(true);
        return true;
    }

    private AgentManagedState StartNext(AgentManagedState current)
    {
        if (current.Snapshot.Status == "Stopped" || current.Tasks.Any(static task => task.Snapshot.Status == "Running")) { return current; }
        var next = current.Tasks.FirstOrDefault(static task => task.Snapshot.Status == "Queued");
        if (next is null) { return current; }
        var worker = AgentLifecycle.Worker(Id, next.Snapshot.TaskId);
        var signal = Announce(Signal.Create(AgentLifecycle.Start, JsonSerializer.Serialize(new AgentTaskStart(Id, next.Snapshot.TaskId), AgentLifecycle.Json)), worker);
        next = next with { WorkSignal = signal, Snapshot = next.Snapshot with { Status = "Running", StartedAt = TimeProvider.GetUtcNow() } };
        return current with { Tasks = current.Tasks.Select(task => task.Snapshot.TaskId == next.Snapshot.TaskId ? next : task).ToArray() };
    }

    private Task QueuePendingAsync(AgentManagedState current) => current.Snapshot.Status == "Stopped"
        ? Task.CompletedTask : QueueAllAsync(current.Tasks.Where(static task => task.Snapshot.Status == "Queued"));

    private async Task QueueAllAsync(IEnumerable<AgentTaskEntry> tasks)
    {
        foreach (var task in tasks) { await QueueAsync(task).ConfigureAwait(true); }
    }

    private async Task QueueAsync(AgentTaskEntry task)
    {
        var delivery = new SignalDelivery(Signal.Create(AgentLifecycle.Pump, "{}"), task.PumpSignal,
            new CorrelationId(task.PumpSignal.Value), null, Id, 1, task.Snapshot.QueuedAt);
        var admission = await Deliver(delivery).ConfigureAwait(true);
        if (admission == DeliveryAdmission.Busy) { throw new InvalidOperationException("The agent queue is busy. Retry the same command id."); }
    }

    private AgentTaskEntry NewTask(string taskId, string message) => new(
        new(taskId, "Queued", message, null, null, TimeProvider.GetUtcNow(), null, null), AgentLifecycle.Hash(message),
        AgentLifecycle.Signal(Id + "/pump/" + taskId));

    private AgentManagedState Managed() => State?.Managed ?? throw new InvalidOperationException("This agent has not been initialized by an agent builder.");
    private static AgentSnapshot Snapshot(AgentManagedState current) => current.Snapshot with
    {
        Status = current.Snapshot.Status == "Stopped" ? "Stopped" : current.Tasks.Any(static task => task.Snapshot.Status is "Running" or "Queued") ? "Running" : "Ready",
        Tasks = current.Tasks.Select(static task => task.Snapshot).ToArray(),
    };
    private Task SaveManagedAsync(AgentManagedState current, CancellationToken cancellationToken = default)
        => SaveAsync((State ?? new AgentState([])) with { Managed = current }, cancellationToken);

    private static void CheckReceipt(AgentManagedState current, CommandId commandId, string hash)
    {
        if (current.Receipts.Concat(current.StopReceipts ?? []).FirstOrDefault(receipt => receipt.CommandId == commandId) is { } receipt
            && receipt.RequestHash != hash)
        {
            throw new ArgumentException("This command id already identifies a different agent operation.", nameof(commandId));
        }
    }

    private static AgentManagedState Receipt(AgentManagedState current, CommandId commandId, string hash, string? taskId)
    {
        if (current.Receipts.Any(receipt => receipt.CommandId == commandId)) { return current; }
        if (current.Receipts.Count >= AgentLifecycle.MaxReceipts) { throw new InvalidOperationException("This agent reached its command receipt limit."); }
        return current with { Receipts = [.. current.Receipts, new(commandId, hash, taskId)] };
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (await ReceiveManagedAsync(delivery, cancellationToken).ConfigureAwait(true))
        {
            return;
        }
        if (delivery.Signal.Type is not (AIVocabulary.Ask or AIVocabulary.Turn))
        {
            return;
        }

        // A tool runs on whatever thread the function-invocation loop happens to be on. Grain
        // state may only be touched on the grain's own scheduler, so every tool that reaches the
        // graph hops back to the scheduler this turn started on.
        var turnScheduler = TaskScheduler.Current;
        var instruct = Bodies.Instruct(await LatestBodyAsync(AIVocabulary.Instruct).ConfigureAwait(true));

        IChatClient client;
        var ownsClient = false;
        try
        {
            client = Providers.Resolve(ServiceProvider, instruct.Provider, instruct.Model, out ownsClient);
        }
        catch (SignalRejectedException rejected)
        {
            // Advice, not a crash: a thrown reaction would be retried forever with the same
            // error, and the asker would never hear why.
            await ReplyAsync(delivery, rejected.Message, cancellationToken).ConfigureAwait(true);
            return;
        }

        using var ownedClient = ownsClient ? client : null;
        AgentState next;
        try
        {
            var invoker = ServiceProvider.GetRequiredService<INeuronInvoker>();
            var nativeTools = ServiceProvider.GetRequiredService<NativeTools>();
            var operations = new McpOperations(GrainFactory, invoker);
            var tools = new List<AITool>(BrainTools.For(this, operations, invoker)
                .Concat(instruct.Tools.Where(name => !nativeTools.Contains(name)).Distinct(StringComparer.Ordinal)
                    .SelectMany(name => TypedFunctionsFor(invoker, name)))
                .Concat(nativeTools.Resolve(instruct.Tools))
                .Select(function => new TurnBoundFunction(function, turnScheduler)));

            var functionClient = client.GetService<FunctionInvokingChatClient>();
            if (functionClient is null)
            {
                functionClient = new FunctionInvokingChatClient(client,
                    ServiceProvider.GetService<ILoggerFactory>(), ServiceProvider);
                client = functionClient;
            }
            // Permanent rejections are already tool results; thrown failures must reach the drain.
            functionClient.MaximumConsecutiveErrorsPerRequest = 0;

            var agent = new ChatClientAgent(
                client,
                new ChatClientAgentOptions
                {
                    Name = Id.Name,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instruct.System,
                        Tools = tools,
                        ModelId = instruct.Model,
                    },
                },
                ServiceProvider.GetService<ILoggerFactory>(),
                ServiceProvider);

            var correlation = delivery.CorrelationId.ToString();
            var session = await LoadSessionAsync(agent, correlation, cancellationToken).ConfigureAwait(true);
            var input = delivery.Signal.Type == AIVocabulary.Turn
                ? await TurnContextAsync(delivery).ConfigureAwait(true)
                : Bodies.Text(delivery.Signal.Body);

            Microsoft.Agents.AI.AgentResponse response;
            try
            {
                response = await agent.RunAsync(input, session, options: null, cancellationToken).ConfigureAwait(true);
            }
            catch (TimeoutException timeout)
            {
                // The function loop wraps tool failures in AggregateException, including tool timeouts.
                throw new ModelTimeoutException(timeout);
            }

            var text = response.Text ?? string.Empty;
            next = await SessionStateAsync(agent, correlation, session, cancellationToken).ConfigureAwait(true);
            AnnounceReply(delivery, text);
        }
        // A model that times out cancels with a TaskCanceledException that has nothing to do
        // with this turn's token. Letting it escape would leave the cursor in place and the
        // drain would retry the same timeout forever, so it answers like any other failure.
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A failure to reach or persist escapes instead: the drain retries the whole
            // reaction, where answering here would end the conversation over a passing fault.
            if (TransientFailure.Covers(failure))
            {
                throw;
            }

            ServiceProvider.GetService<ILogger<AgentNeuron>>()?.LogError(failure, "Agent {Neuron} failed to answer.", Id);
            AnnounceReply(delivery, failure.Message);
            next = State ?? new AgentState([]);
        }
        await SaveAsync(next, cancellationToken).ConfigureAwait(true);
    }

    private AIFunction[] TypedFunctionsFor(INeuronInvoker invoker, string name)
    {
        if (_typedFunctions.TryGetValue(name, out var functions))
        {
            return functions;
        }

        // A tools entry that is neither a native tool nor a neuron name contributes nothing,
        // for the same reason NativeTools skips a name nobody registered.
        functions = NeuronId.TryParse(name, out var neuron)
            ? [.. TypedNeuronFunctions.For(invoker, neuron, _takenFunctionNames)]
            : [];
        _typedFunctions.Add(name, functions);
        return functions;
    }

    // ---- what the tools call ----

    internal Task<string> FireSignalAsync(string type, string body, string? to, string? correlation)
    {
        NeuronId? target = null;
        if (!string.IsNullOrWhiteSpace(to))
        {
            target = NeuronId.TryParse(to, out var parsed)
                ? parsed
                : throw new ArgumentException($"'{to}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", nameof(to));
        }

        CorrelationId? tie = null;
        if (!string.IsNullOrWhiteSpace(correlation))
        {
            tie = Guid.TryParse(correlation, out var value)
                ? new CorrelationId(value)
                : throw new ArgumentException($"'{correlation}' is not a correlation id.", nameof(correlation));
        }

        var signalId = Announce(Signal.Create(type, body), target, tie);
        return Task.FromResult(JsonSerializer.Serialize(new { signalId = signalId.ToString(), status = "announced" }, ToolJson));
    }

    internal async Task<string> ChangeSynapseAsync(string from, string to, string type, bool connect)
    {
        var source = Parse(from, nameof(from));
        var target = Parse(to, nameof(to));

        // A synapse carries a signal type, so the type must be vocabulary before the edge exists.
        _ = Signal.Create(type, "{}");

        if (source == Id)
        {
            await (connect ? Connect(target, type) : Disconnect(target, type)).ConfigureAwait(true);
        }
        else
        {
            var neuron = GrainFactory.GetGrain<INeuron>(source.ToGrainId());
            await (connect ? neuron.Connect(target, type) : neuron.Disconnect(target, type)).ConfigureAwait(true);
        }

        return $"{(connect ? "connected" : "disconnected")} {from} --{type}--> {to}";
    }

    // ---- the turn ----

    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private async Task<string> LatestBodyAsync(string type)
    {
        var latest = await ReadState().ConfigureAwait(true);
        return latest.FirstOrDefault(d => d.Signal.Type == type)?.Signal.Body ?? string.Empty;
    }

    private Task ReplyAsync(SignalDelivery delivery, string text, CancellationToken cancellationToken)
    {
        AnnounceReply(delivery, text);
        return SaveAsync(State ?? new AgentState([]), cancellationToken);
    }

    private void AnnounceReply(SignalDelivery delivery, string text)
        => Announce(delivery.Signal.Type == AIVocabulary.Turn
            ? Signal.FromJson(AIVocabulary.Said, new SaidBody(Id.ToString(), text), AIJson.Default.SaidBody)
            : Signal.FromJson(AIVocabulary.Reply, new TextBody(text), AIJson.Default.TextBody), delivery.Source, delivery.CorrelationId);

    // A turn carries no transcript: the participant reads the source's incoming journal for
    // its own correlation and speaks next.
    private async Task<string> TurnContextAsync(SignalDelivery delivery)
    {
        var read = await GrainFactory.GetGrain<INeuron>(delivery.Source.ToGrainId())
            .ReadJournal(JournalKind.Incoming, 0)
            .ConfigureAwait(true);

        var context = new StringBuilder();
        foreach (var entry in read.Delta.Where(e => e.CorrelationId == delivery.CorrelationId))
        {
            if (entry.Signal.Type == AIVocabulary.Ask)
            {
                context.AppendLine(Bodies.Text(entry.Signal.Body));
            }
        }

        foreach (var entry in read.Delta.Where(e => e.CorrelationId == delivery.CorrelationId && e.Signal.Type == AIVocabulary.Said))
        {
            var said = Bodies.SaidLine(entry.Signal.Body);
            if (said.Length > 0)
            {
                context.AppendLine(said);
            }
        }

        context.Append(CultureInfo.InvariantCulture, $"You are {Id}. Speak next.");
        return context.ToString();
    }

    // ---- sessions ----

    private async Task<AgentSession> LoadSessionAsync(AIAgent agent, string correlation, CancellationToken cancellationToken)
    {
        if (State?.Sessions.FirstOrDefault(s => string.Equals(s.Correlation, correlation, StringComparison.Ordinal)) is { } entry)
        {
            using var document = JsonDocument.Parse(entry.SessionJson);
            return await agent.DeserializeSessionAsync(document.RootElement, cancellationToken: cancellationToken).ConfigureAwait(true);
        }

        return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task<AgentState> SessionStateAsync(AIAgent agent, string correlation, AgentSession session, CancellationToken cancellationToken)
    {
        var json = (await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(true)).GetRawText();
        var current = State;
        var sessions = current is null ? [] : new List<AgentSessionEntry>(current.Sessions);
        sessions.RemoveAll(s => string.Equals(s.Correlation, correlation, StringComparison.Ordinal));
        sessions.Add(new AgentSessionEntry(correlation, json));
        Trim(sessions, AgentState.MaxSessions);

        return new AgentState(sessions);
    }

    private static void Trim<T>(List<T> entries, int keep)
    {
        if (entries.Count > keep)
        {
            entries.RemoveRange(0, entries.Count - keep);
        }
    }

    // ---- plumbing ----

    private static NeuronId Parse(string text, string parameter)
        => NeuronId.TryParse(text, out var id)
            ? id
            : throw new ArgumentException($"'{text}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", parameter);
}
