using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using DigitalBrain.Modules.Sdk.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.UI;

[GrainType("chat")]
internal sealed class Chat : Neuron, IChat
{
    private const string CommandLogName = "chat.command-log";
    private const string TranscriptName = "chat.transcript";
    private const string TurnLogName = "chat.turn-log";
    private const string QueueStateName = "chat.turn-queue";
    private const int RememberedCommands = 64;
    private const int RetainedTurns = 64;
    private const int RetainedTurnRecords = 64;

    private static readonly ExecutionPolicy TurnPolicy = new(
        MaximumAttempts: 1,
        RetryDelay: TimeSpan.FromSeconds(1),
        Deadline: null);

    private readonly IDurableList<byte[]> _commandLog;
    private readonly IDurableList<byte[]> _transcript;
    private readonly IDurableList<byte[]> _turnLog;
    private readonly IDurableValue<byte[]> _queueState;
    private readonly Serializer<OwnerCommand> _commands;
    private readonly Serializer<ChatTurn> _turns;
    private readonly Serializer<DurableTurnRecord> _turnRecords;
    private readonly Serializer<TurnQueueState> _queues;

    public Chat()
    {
        _commandLog = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(CommandLogName);
        _transcript = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(TranscriptName);
        _turnLog = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(TurnLogName);
        _queueState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(QueueStateName);
        _commands = ServiceProvider.GetRequiredService<Serializer<OwnerCommand>>();
        _turns = ServiceProvider.GetRequiredService<Serializer<ChatTurn>>();
        _turnRecords = ServiceProvider.GetRequiredService<Serializer<DurableTurnRecord>>();
        _queues = ServiceProvider.GetRequiredService<Serializer<TurnQueueState>>();
    }

    public async Task<TurnAccepted> Send(SendMessage message)
        => await EnqueueTurnAsync(message).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Enqueue + start Execution; the AI run is independent of cancellationToken.
        // This stream is a pure observer surface — abort detaches without cancelling the turn.
        _ = await EnqueueTurnAsync(message).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    public async Task Cancel(CancelTurn command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireActor(command.Actor, "cancel-turn");

        if (command.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(command));
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == command.TurnId.Value);
        if (index < 0)
        {
            // Idempotent: unknown turn is a settled no-op for duplicate cancels after retention.
            return;
        }

        var record = turns[index];
        if (record.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
        {
            return;
        }

        if (record.Status == ChatTurnStatus.Pending)
        {
            turns[index] = record with { Status = ChatTurnStatus.Cancelled, Revision = record.Revision + 1 };
            SaveTurns(turns);
            var queue = LoadQueue();
            queue.PendingTurnIds.Remove(record.TurnId);
            SaveQueue(queue);
            await EmitAsync(new TurnLifecycle(
                new TurnId(record.TurnId),
                new CommandId(record.CommandId),
                Id,
                ChatTurnStatus.Cancelled,
                "queued-cancel")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Running: cancel the active Execution (versioned).
        if (string.IsNullOrWhiteSpace(record.ExecutionName))
        {
            throw new NeuronAuthorizationException(
                $"Chat '{Id}' cannot cancel turn '{command.TurnId}' without an execution name.");
        }

        var execution = GrainFactory.GetGrain<IExecution>(
            NeuronId.For<IExecution>(Id.Owner, record.ExecutionName).ToGrainId());
        var snapshot = await execution.Read().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var expected = command.ExpectedRevision ?? snapshot.Revision;
        await execution.Apply(new ApplyExecution(
            command.CommandId,
            new CancelExecution(),
            ExpectedRevision: expected)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        turns[index] = record with { Status = ChatTurnStatus.Cancelled, Revision = record.Revision + 1 };
        SaveTurns(turns);
        var activeQueue = LoadQueue();
        if (activeQueue.ActiveTurnId == record.TurnId)
        {
            activeQueue = activeQueue with { ActiveTurnId = null, ActiveExecutionName = null };
            SaveQueue(activeQueue);
        }

        await EmitAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            ChatTurnStatus.Cancelled,
            "running-cancel")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<ChatTranscript> Read() => Task.FromResult(new ChatTranscript(Turns()));

    public Task<IReadOnlyList<ChatTurnSnapshot>> ReadTurns()
        => Task.FromResult<IReadOnlyList<ChatTurnSnapshot>>(
            [.. LoadTurns().Select(static turn => new ChatTurnSnapshot(
                new TurnId(turn.TurnId),
                new CommandId(turn.CommandId),
                turn.Text,
                turn.Status,
                turn.ExecutionName))]);

    public async Task HandleAsync(ReadTranscriptRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subject = NeuronId.For<IChat>(Id.Owner, synapse.ChatName);
        var transcript = subject == Id
            ? new ChatTranscript(Turns())
            : await GrainFactory.GetGrain<IChat>(subject.ToGrainId()).Read().WaitAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(
            new TranscriptRead(synapse.CommandId, subject, Trimmed(transcript, synapse.MaxTurns)),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(Note synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Text))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses an empty note.");
        }

        Remember(new ChatTurn(FromUser: false, synapse.Text));
        await EmitAsync(new Responded(CommandId.New(), Id, synapse.Text, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(TimerCard synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Label))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses a timer card without a label.");
        }

        ChatTimerOffer[] offers = [new ChatTimerOffer(synapse.Label, synapse.DueAt)];
        Remember(new ChatTurn(FromUser: false, synapse.Label, Timers: offers));
        await EmitAsync(new Responded(CommandId.New(), Id, synapse.Label, Timers: offers, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    protected override async Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        if (synapse is CompleteTurnWork complete)
        {
            await CompleteTurnAsync(complete).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (synapse is not AuthorizationRequired required)
        {
            await base.OnUnboundSynapseAsync(synapse, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(required.ServerDisplayName))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses a sign-in offer without a server name.");
        }

        var label = $"Sign in via {required.ServerDisplayName}";
        var buttonId = $"sign-in-{required.ServerKey}";
        var action = required.SignInUrl.AbsoluteUri;
        ChatButtonOffer[] buttons = [new ChatButtonOffer(buttonId, label, action)];
        var text = $"{required.ServerDisplayName} needs sign-in before that request can continue.";
        Remember(new ChatTurn(FromUser: false, text, buttons));
        await EmitAsync(new Responded(required.CommandId, Id, text, buttons, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task CompleteTurnAsync(CompleteTurnWork complete)
    {
        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == complete.TurnId);
        if (index < 0)
        {
            return;
        }

        var record = turns[index];
        if (record.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
        {
            // Idempotent completion after restart / duplicate delivery.
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        turns[index] = record with
        {
            Status = complete.Status,
            Revision = record.Revision + 1,
        };
        SaveTurns(turns);

        if (complete.Status == ChatTurnStatus.Completed
            && !string.IsNullOrWhiteSpace(complete.Text))
        {
            Remember(new ChatTurn(FromUser: false, complete.Text));
            await EmitAsync(new Responded(
                complete.CommandId,
                Id,
                complete.Text,
                Author: complete.Author ?? string.Empty))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await EmitAsync(new TurnLifecycle(
            new TurnId(complete.TurnId),
            complete.CommandId,
            Id,
            complete.Status,
            complete.Detail)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var queue = LoadQueue();
        if (queue.ActiveTurnId == complete.TurnId)
        {
            queue = queue with { ActiveTurnId = null, ActiveExecutionName = null };
            SaveQueue(queue);
        }

        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<TurnAccepted> EnqueueTurnAsync(SendMessage message)
    {
        RequireActor(message.Actor, "send");
        if (!IsUnseenCommand(message))
        {
            var existing = LoadTurns().FirstOrDefault(turn => turn.CommandId == message.CommandId.Value);
            if (existing is not null)
            {
                return new TurnAccepted(
                    new TurnId(existing.TurnId),
                    message.CommandId,
                    existing.Status);
            }

            throw new NeuronAuthorizationException(
                $"Chat '{Id}' already saw command '{message.CommandId}' without a retained turn record.");
        }

        await RememberOwnerTurnAsync(message).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var turnId = Guid.NewGuid();
        var record = new DurableTurnRecord(
            turnId,
            message.CommandId.Value,
            message.Text,
            message.Actor!,
            ChatTurnStatus.Pending,
            ExecutionName: null,
            Revision: 0);
        var turns = LoadTurns();
        turns.Add(record);
        TrimTurns(turns);
        SaveTurns(turns);

        var queue = LoadQueue();
        queue.PendingTurnIds.Add(turnId);
        SaveQueue(queue);

        await EmitAsync(new TurnLifecycle(
            new TurnId(turnId),
            message.CommandId,
            Id,
            ChatTurnStatus.Pending)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var started = LoadTurns().First(turn => turn.TurnId == turnId);
        return new TurnAccepted(new TurnId(turnId), message.CommandId, started.Status);
    }

    private async Task TryStartNextAsync()
    {
        var queue = LoadQueue();
        if (queue.ActiveTurnId is not null)
        {
            return;
        }

        if (queue.PendingTurnIds.Count == 0)
        {
            return;
        }

        var nextTurnId = queue.PendingTurnIds[0];
        queue.PendingTurnIds.RemoveAt(0);

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == nextTurnId);
        if (index < 0)
        {
            SaveQueue(queue);
            return;
        }

        var record = turns[index];
        if (record.Status != ChatTurnStatus.Pending)
        {
            SaveQueue(queue);
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var executionName = $"chat-turn-{record.TurnId:N}";
        var worker = ChatTurnWorker.ForChat(Id);
        var goal = new ChatTurnGoal(
            record.TurnId,
            new CommandId(record.CommandId),
            record.Text,
            record.Actor,
            Id);

        var execution = GrainFactory.GetGrain<IExecution>(
            NeuronId.For<IExecution>(Id.Owner, executionName).ToGrainId());
        await execution.Apply(new ApplyExecution(
            CommandId.New(),
            new StartExecution(goal, worker, TurnPolicy)))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        turns[index] = record with
        {
            Status = ChatTurnStatus.Running,
            ExecutionName = executionName,
            Revision = record.Revision + 1,
        };
        SaveTurns(turns);

        queue = queue with { ActiveTurnId = record.TurnId, ActiveExecutionName = executionName };
        SaveQueue(queue);

        await EmitAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            ChatTurnStatus.Running,
            executionName)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static ChatTranscript Trimmed(ChatTranscript transcript, int? maxTurns)
        => maxTurns is not { } cap || transcript.Turns.Count <= cap
            ? transcript
            : new ChatTranscript([.. transcript.Turns.Skip(transcript.Turns.Count - cap)]);

    private IReadOnlyList<ChatTurn> Turns() => [.. _transcript.Select(_turns.Deserialize)];

    private bool IsUnseenCommand(SendMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);
        if (message.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(message));
        }

        for (var remembered = _commandLog.Count - 1; remembered >= 0; remembered--)
        {
            var command = _commands.Deserialize(_commandLog[remembered]);
            if (command.CommandId != message.CommandId.Value)
            {
                continue;
            }

            if (!string.Equals(command.Text, message.Text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A chat command id cannot be reused with different text.");
            }

            return false;
        }

        return true;
    }

    private Task RememberOwnerTurnAsync(SendMessage message)
    {
        Remember(message.CommandId, message.Text, message.Actor);
        Remember(new ChatTurn(FromUser: true, message.Text));
        return EmitAsync(new UserMessaged(message.CommandId, Id, message.Text, message.Actor));
    }

    private void Remember(CommandId commandId, string text, ActorContext? actor)
        => Append(
            _commandLog,
            _commands.SerializeToArray(new OwnerCommand(commandId.Value, text, actor)),
            RememberedCommands);

    private void Remember(ChatTurn turn)
        => Append(_transcript, _turns.SerializeToArray(turn), RetainedTurns);

    private List<DurableTurnRecord> LoadTurns()
        => [.. _turnLog.Select(_turnRecords.Deserialize)];

    private void SaveTurns(List<DurableTurnRecord> turns)
    {
        while (_turnLog.Count > 0)
        {
            _turnLog.RemoveAt(_turnLog.Count - 1);
        }

        foreach (var turn in turns)
        {
            _turnLog.Add(_turnRecords.SerializeToArray(turn));
        }
    }

    private void TrimTurns(List<DurableTurnRecord> turns)
    {
        while (turns.Count > RetainedTurnRecords)
        {
            turns.RemoveAt(0);
        }
    }

    private TurnQueueState LoadQueue()
    {
        if (_queueState.Value is not { Length: > 0 } bytes)
        {
            return new TurnQueueState([], null, null);
        }

        return _queues.Deserialize(bytes);
    }

    private void SaveQueue(TurnQueueState queue)
        => _queueState.Value = _queues.SerializeToArray(queue);

    private static void RequireActor(ActorContext? actor, string command)
    {
        if (actor is null)
        {
            throw new NeuronAuthorizationException(
                $"Chat refuses durable owner command '{command}' without an Actor stamp.");
        }

        if (string.IsNullOrWhiteSpace(actor.Username))
        {
            throw new NeuronAuthorizationException(
                $"Chat refuses durable owner command '{command}' with an empty actor username.");
        }
    }

    private static void Append(IDurableList<byte[]> entries, byte[] entry, int retained)
    {
        entries.Add(entry);
        while (entries.Count > retained)
        {
            entries.RemoveAt(0);
        }
    }

    [GenerateSerializer]
    internal sealed record OwnerCommand(
        [property: Id(0)] Guid CommandId,
        [property: Id(1)] string Text,
        [property: Id(2)] ActorContext? Actor = null);

    [GenerateSerializer]
    internal sealed record DurableTurnRecord(
        [property: Id(0)] Guid TurnId,
        [property: Id(1)] Guid CommandId,
        [property: Id(2)] string Text,
        [property: Id(3)] ActorContext Actor,
        [property: Id(4)] ChatTurnStatus Status,
        [property: Id(5)] string? ExecutionName,
        [property: Id(6)] long Revision);

    [GenerateSerializer]
    internal sealed record TurnQueueState(
        [property: Id(0)] List<Guid> PendingTurnIds,
        [property: Id(1)] Guid? ActiveTurnId,
        [property: Id(2)] string? ActiveExecutionName);
}
