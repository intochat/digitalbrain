using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Memory;
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

    // One turn is one awaited worker call; the budget mirrors the kernel SSE edge and the
    // call's own ResponseTimeout. The deadline timer is the belt for a call that never
    // resumes; the grace keeps the two belts from racing on an honest slow finish.
    private static readonly TimeSpan TurnBudget =
        TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture);
    private static readonly TimeSpan TurnDeadlineGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TurnDeadlineCheckPeriod = TimeSpan.FromSeconds(15);

    private readonly IDurableList<byte[]> _commandLog;
    private readonly IDurableList<byte[]> _transcript;
    private readonly IDurableList<byte[]> _turnLog;
    private readonly IDurableValue<byte[]> _queueState;
    private readonly Serializer<OwnerCommand> _commands;
    private readonly Serializer<ChatTurn> _turns;
    private readonly Serializer<DurableTurnRecord> _turnRecords;
    private readonly Serializer<TurnQueueState> _queues;

    // The in-flight worker call, fire-and-tracked: the task settles the turn when the call
    // returns or throws; the token is the turn-scoped cancel; the timer fails a call that
    // outlives its budget. All in-memory — a restarted activation reconciles durably instead.
    private Task? _activeCall;
    private Guid? _activeCallTurnId;
    private CancellationTokenSource? _activeCallCancellation;
    private DateTimeOffset? _activeCallStartedAt;
    private IGrainTimer? _turnDeadlineTimer;

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

    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await FailTurnInterruptedByRestartAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task<TurnAccepted> Send(SendMessage message)
        => await EnqueueTurnAsync(message).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Enqueue + start the turn; the AI run is independent of cancellationToken.
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
        if (IsTerminal(record.Status))
        {
            return;
        }

        if (record.Status == ChatTurnStatus.Cancelling)
        {
            // Already flagged; the in-flight call's cancellation settles the turn.
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

        // Running head with no tracked call can only mean the tracking activation died and a
        // reconcile has not settled it yet — settle here rather than cancelling nothing.
        if (_activeCallTurnId != record.TurnId)
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Cancelled, result: null, "cancelled")
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Running: flag first so Cancelling always precedes Cancelled in the journal, then
        // trip the call's token; the call continuation settles the turn as Cancelled.
        var cancellation = _activeCallCancellation;
        turns[index] = record with { Status = ChatTurnStatus.Cancelling, Revision = record.Revision + 1 };
        SaveTurns(turns);
        await EmitAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            ChatTurnStatus.Cancelling,
            "running-cancel")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellation?.Cancel();
    }

    public Task<ChatTranscript> Read() => Task.FromResult(new ChatTranscript(Turns()));

    public Task<IReadOnlyList<ChatTurnSnapshot>> ReadTurns()
        => Task.FromResult<IReadOnlyList<ChatTurnSnapshot>>(
            [.. LoadTurns().Select(static turn => new ChatTurnSnapshot(
                new TurnId(turn.TurnId),
                new CommandId(turn.CommandId),
                turn.Text,
                turn.Status))]);

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

    public async Task HandleAsync(ChartCard synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Title))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses a chart card without a title.");
        }

        // Title names the chart instance the points targeted (the corpus grammar's chart-card
        // invariant), so it doubles as the chart entity's instance name.
        var state = await GrainFactory
            .GetGrain<IChart>(EntityId.For<IChart>(Id.Owner, synapse.Title).ToGrainId())
            .Read()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        ChatChartPoint[] points = state is null
            ? []
            : [.. state.Points.Select(static point => new ChatChartPoint(point.Label, point.Value))];

        ChatChartOffer[] offers = [new ChatChartOffer(synapse.Title, points, "bar")];
        Remember(new ChatTurn(FromUser: false, synapse.Title, Charts: offers));
        await EmitAsync(new Responded(CommandId.New(), Id, synapse.Title, Charts: offers, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // A fresh activation cannot resume an in-flight worker call (the awaiting task died with
    // the previous activation), so a durably Running head is settled as Failed.
    private async Task FailTurnInterruptedByRestartAsync()
    {
        var queue = LoadQueue();
        if (queue.ActiveTurnId is not { } activeTurnId)
        {
            return;
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == activeTurnId);
        if (index < 0 || IsTerminal(turns[index].Status))
        {
            SaveQueue(queue with { ActiveTurnId = null });
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        await SettleTurnAsync(activeTurnId, ChatTurnStatus.Failed, result: null, "turn-interrupted")
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        var running = record with { Status = ChatTurnStatus.Running, Revision = record.Revision + 1 };
        turns[index] = running;
        SaveTurns(turns);
        SaveQueue(queue with { ActiveTurnId = record.TurnId });
        // Stay activated for the whole attempt so idle deactivation cannot orphan a live head.
        DelayDeactivation(TurnBudget + TurnDeadlineGrace + TurnDeadlineCheckPeriod);

        // Running is committed to the journal BEFORE the call starts, so a instantly-settling
        // worker can never put Responded/Completed ahead of Running.
        await EmitAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            ChatTurnStatus.Running)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        StartTurnCall(running);
    }

    private void StartTurnCall(DurableTurnRecord record)
    {
        _activeCallCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _activeCallCancellation = cancellation;
        _activeCallTurnId = record.TurnId;
        _activeCallStartedAt = TimeProvider.GetUtcNow();
        _activeCall = RunTurnAsync(record, cancellation.Token);
        _turnDeadlineTimer ??= this.RegisterGrainTimer(
            FailOverBudgetTurnAsync,
            dueTime: TurnDeadlineCheckPeriod,
            period: TurnDeadlineCheckPeriod);
    }

    private async Task RunTurnAsync(DurableTurnRecord record, CancellationToken cancellationToken)
    {
        try
        {
            var worker = GrainFactory.GetGrain<IChatTurnWorker>(ChatTurnWorker.ForChat(Id).ToGrainId());
            var goal = new ChatTurnGoal(
                record.TurnId,
                new CommandId(record.CommandId),
                record.Text,
                record.Actor,
                Id);
            var result = await worker.RunAsync(goal, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Completed, result, detail: null)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Cancelled, result: null, "cancelled")
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Failed, result: null, failure.Message)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private async Task FailOverBudgetTurnAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_activeCallTurnId is not { } turnId || _activeCallStartedAt is not { } startedAt)
        {
            _turnDeadlineTimer?.Dispose();
            _turnDeadlineTimer = null;
            return;
        }

        if (TimeProvider.GetUtcNow() - startedAt < TurnBudget + TurnDeadlineGrace)
        {
            return;
        }

        if (_activeCall is { IsCompleted: true })
        {
            // The call already returned; its settle continuation is queued — let it land.
            return;
        }

        // Fail durably FIRST so the call continuation's late cancellation lands on a settled
        // turn, then trip the token to reclaim the worker. The source is detached before the
        // settle so the next turn's start cannot dispose it out from under this callback.
        var cancellation = _activeCallCancellation;
        _activeCallCancellation = null;
        await SettleTurnAsync(
            turnId,
            ChatTurnStatus.Failed,
            result: null,
            $"turn-budget-exceeded after {TurnBudget}").ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    // The single settle point for every turn outcome: worker result, worker failure,
    // cancellation, budget overrun, restart reconcile. Emits the frozen journal footprint —
    // Responded (Completed with an answer only) then the terminal TurnLifecycle — and
    // advances the FIFO.
    private async Task SettleTurnAsync(
        Guid turnId,
        ChatTurnStatus status,
        ChatTurnResult? result,
        string? detail)
    {
        if (_activeCallTurnId == turnId)
        {
            _activeCallTurnId = null;
            _activeCallStartedAt = null;
            _activeCall = null;
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == turnId);
        if (index < 0 || IsTerminal(turns[index].Status))
        {
            // Already settled (deadline beat the continuation, or retention dropped it) —
            // only make sure the queue is not stuck on it.
            var queueDone = LoadQueue();
            if (queueDone.ActiveTurnId == turnId)
            {
                SaveQueue(queueDone with { ActiveTurnId = null });
                await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return;
        }

        var record = turns[index];
        turns[index] = record with { Status = status, Revision = record.Revision + 1 };
        SaveTurns(turns);

        if (status == ChatTurnStatus.Completed)
        {
            await TryEmitRespondedAsync(record, result)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await EmitAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            status,
            detail)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var queue = LoadQueue();
        if (queue.ActiveTurnId == record.TurnId)
        {
            SaveQueue(queue with { ActiveTurnId = null });
        }

        DelayDeactivation(TimeSpan.FromMinutes(1));
        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task TryEmitRespondedAsync(DurableTurnRecord record, ChatTurnResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Answer))
        {
            return;
        }

        await EmitAsync(new Responded(
            new CommandId(record.CommandId),
            Id,
            result.Answer,
            Author: result.Author))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var existing = Turns();
        if (!existing.Any(turn => !turn.FromUser
            && string.Equals(turn.Text, result.Answer, StringComparison.Ordinal)))
        {
            Remember(new ChatTurn(FromUser: false, result.Answer));
        }

        await SendAsync(
            IFactMemory.ForOwner(Id.Owner),
            new StoreFact(
                CommandId.New(),
                Kind: "chat.responded",
                Text: result.Answer,
                Correlation: record.CommandId.ToString("n"),
                At: TimeProvider.GetUtcNow()))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static bool IsTerminal(ChatTurnStatus status)
        => status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled;

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
            return new TurnQueueState([], null);
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
}
