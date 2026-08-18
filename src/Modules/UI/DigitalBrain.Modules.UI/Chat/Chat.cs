using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Corpus;
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
internal sealed class Chat : Neuron, IChat, IRemindable
{
    private const string CommandLogName = "chat.command-log";
    private const string TranscriptName = "chat.transcript";
    private const string TurnLogName = "chat.turn-log";
    private const string QueueStateName = "chat.turn-queue";
    private const string WaitingDeadlineReminderName = "chat.waiting-deadline";
    private const int RememberedCommands = 64;
    private const int RetainedTurns = 64;
    private const int RetainedTurnRecords = 64;
    // Owner-path unstick for Execution parks (OutcomeUncertain / Waiting) — mirrors kernel liveness scale.
    private static readonly TimeSpan WaitingPolicyDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

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
    // Wake-ups queued during a delivery turn; re-Read runs on a grain timer so it never
    // nests inside Execution recovery / worker Accept (green-b deadlock class).
    private readonly List<ExecutionTerminal> _pendingTerminalWakeups = [];
    private IGrainTimer? _terminalWakeupTimer;

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
        await ReconcileActiveExecutionAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        if (record.Status == ChatTurnStatus.Cancelling)
        {
            // Already asked the kernel to cancel; wait for the terminal bridge.
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

        // Running: cancel the active Execution; stay head until the terminal bridge advances.
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

        turns[index] = record with { Status = ChatTurnStatus.Cancelling, Revision = record.Revision + 1 };
        SaveTurns(turns);

        await EmitAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            ChatTurnStatus.Cancelling,
            "running-cancel")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        // Do NOT clear ActiveTurnId and do NOT TryStartNext — bridge only.
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

    public async Task HandleAsync(ChartCard synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Title))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses a chart card without a title.");
        }

        // Title names the chart instance the points targeted (the corpus grammar's chart-card
        // invariant), so it doubles as the ChartEntity's instance name.
        var state = await GrainFactory
            .GetGrain<IChartEntity>(EntityId.For<IChartEntity>(Id.Owner, synapse.Title).ToGrainId())
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

    protected override async Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        if (synapse is ExecutionTerminal terminal)
        {
            await ReconcileFromExecutionTerminalAsync(terminal)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

    private Task ReconcileFromExecutionTerminalAsync(ExecutionTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        if (terminal.ExecutionId.Owner != Id.Owner)
        {
            return Task.CompletedTask;
        }

        var turns = LoadTurns();
        var queue = LoadQueue();
        if (FindTurnIndexForExecution(turns, queue, terminal.ExecutionId.Name) < 0)
        {
            // Unknown / mismatched ExecutionId — ignore settled.
            return Task.CompletedTask;
        }

        // Queue the wake-up; re-Read + apply on a timer turn (not nested in this delivery).
        _pendingTerminalWakeups.Add(terminal);
        _terminalWakeupTimer ??= this.RegisterGrainTimer(
            ProcessPendingTerminalWakeupsAsync,
            dueTime: TimeSpan.FromMilliseconds(1),
            period: TimeSpan.FromMilliseconds(50));
        return Task.CompletedTask;
    }

    private async Task ProcessPendingTerminalWakeupsAsync(CancellationToken cancellationToken)
    {
        if (_pendingTerminalWakeups.Count == 0)
        {
            _terminalWakeupTimer?.Dispose();
            _terminalWakeupTimer = null;
            return;
        }

        var batch = _pendingTerminalWakeups.ToArray();
        _pendingTerminalWakeups.Clear();

        foreach (var terminal in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyTerminalWakeupFromKernelReadAsync(terminal)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (_pendingTerminalWakeups.Count == 0)
        {
            _terminalWakeupTimer?.Dispose();
            _terminalWakeupTimer = null;
        }
    }

    private async Task ApplyTerminalWakeupFromKernelReadAsync(ExecutionTerminal terminal)
    {
        var turns = LoadTurns();
        var queue = LoadQueue();
        var index = FindTurnIndexForExecution(turns, queue, terminal.ExecutionId.Name);
        if (index < 0)
        {
            return;
        }

        // Wake-up only: kernel Read is authority. Forged State/Revision not confirmed by Read
        // are ignored settled. Result/Failure always come from the snapshot.
        ExecutionSnapshot snapshot;
        try
        {
            snapshot = await GrainFactory.GetGrain<IExecution>(terminal.ExecutionId.ToGrainId())
                .Read()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            return;
        }

        if (terminal.Revision != snapshot.Revision || terminal.State != snapshot.State)
        {
            return;
        }

        if (snapshot.State == ExecutionState.Waiting)
        {
            await SurfaceWaitingTurnAsync(turns, index, snapshot)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (!IsExecutionTerminal(snapshot.State))
        {
            return;
        }

        await ApplyExecutionSnapshotToTurnAsync(turns, index, snapshot)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, WaitingDeadlineReminderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Chat neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        await FailWaitingTurnAfterPolicyDeadlineAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task ReconcileActiveExecutionAsync()
    {
        var queue = LoadQueue();
        if (queue.ActiveTurnId is null || string.IsNullOrWhiteSpace(queue.ActiveExecutionName))
        {
            return;
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == queue.ActiveTurnId.Value);
        if (index < 0)
        {
            queue = queue with { ActiveTurnId = null, ActiveExecutionName = null };
            SaveQueue(queue);
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var record = turns[index];
        if (record.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
        {
            if (queue.ActiveTurnId == record.TurnId)
            {
                queue = queue with { ActiveTurnId = null, ActiveExecutionName = null };
                SaveQueue(queue);
            }

            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var executionId = NeuronId.For<IExecution>(Id.Owner, queue.ActiveExecutionName!);
        ExecutionSnapshot snapshot;
        try
        {
            snapshot = await GrainFactory.GetGrain<IExecution>(executionId.ToGrainId())
                .Read()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            turns[index] = record with
            {
                Status = ChatTurnStatus.Failed,
                Revision = record.Revision + 1,
            };
            SaveTurns(turns);
            queue = queue with { ActiveTurnId = null, ActiveExecutionName = null };
            SaveQueue(queue);
            DelayDeactivation(TimeSpan.FromMinutes(1));
            await EmitAsync(new TurnLifecycle(
                new TurnId(record.TurnId),
                new CommandId(record.CommandId),
                Id,
                ChatTurnStatus.Failed,
                "execution-unreadable")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (IsExecutionTerminal(snapshot.State))
        {
            await ApplyExecutionSnapshotToTurnAsync(turns, index, snapshot)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (snapshot.State == ExecutionState.Waiting)
        {
            await SurfaceWaitingTurnAsync(turns, index, snapshot)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // After silo restart the worker's in-memory Accept is gone and in-memory
        // reminders may not fire. Drive the kernel to terminal via Cancel so the
        // bridge can advance the queue. Live turns stay warm via DelayDeactivation.
        if (snapshot.State is ExecutionState.Running or ExecutionState.Pending or ExecutionState.Cancelling)
        {
            try
            {
                await GrainFactory.GetGrain<IExecution>(executionId.ToGrainId())
                    .Apply(new ApplyExecution(
                        CommandId.New(),
                        new CancelExecution(),
                        ExpectedRevision: snapshot.Revision))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            catch (NeuronAuthorizationException)
            {
                // Revision race or already terminal — re-Read below.
            }

            try
            {
                snapshot = await GrainFactory.GetGrain<IExecution>(executionId.ToGrainId())
                    .Read()
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            catch
            {
                return;
            }

            if (IsExecutionTerminal(snapshot.State))
            {
                await ApplyExecutionSnapshotToTurnAsync(turns, index, snapshot)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            else if (snapshot.State == ExecutionState.Waiting)
            {
                await SurfaceWaitingTurnAsync(turns, index, snapshot)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            else if (record.Status != ChatTurnStatus.Cancelling)
            {
                turns[index] = record with
                {
                    Status = ChatTurnStatus.Cancelling,
                    Revision = record.Revision + 1,
                };
                SaveTurns(turns);
            }
        }
    }

    private async Task SurfaceWaitingTurnAsync(
        List<DurableTurnRecord> turns,
        int index,
        ExecutionSnapshot snapshot)
    {
        var record = turns[index];
        if (record.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
        {
            return;
        }

        if (record.Status != ChatTurnStatus.Waiting)
        {
            var detail = snapshot.Blocker switch
            {
                OutcomeUncertain => "outcome-uncertain",
                InputRequired => "input-required",
                _ => snapshot.Blocker?.GetType().Name ?? "waiting",
            };
            turns[index] = record with
            {
                Status = ChatTurnStatus.Waiting,
                Revision = record.Revision + 1,
            };
            SaveTurns(turns);
            await EmitAsync(new TurnLifecycle(
                new TurnId(record.TurnId),
                new CommandId(record.CommandId),
                Id,
                ChatTurnStatus.Waiting,
                detail)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        // Keep FIFO head; arm durable policy deadline so the park cannot freeze forever.
        DelayDeactivation(WaitingPolicyDeadline + ReminderPeriod);
        await this.RegisterOrUpdateReminder(
                WaitingDeadlineReminderName,
                WaitingPolicyDeadline,
                ReminderPeriod)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task FailWaitingTurnAfterPolicyDeadlineAsync()
    {
        var queue = LoadQueue();
        if (queue.ActiveTurnId is null || string.IsNullOrWhiteSpace(queue.ActiveExecutionName))
        {
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == queue.ActiveTurnId.Value);
        if (index < 0)
        {
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var record = turns[index];
        if (record.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
        {
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (record.Status is not (ChatTurnStatus.Waiting or ChatTurnStatus.Running or ChatTurnStatus.Cancelling))
        {
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var executionId = NeuronId.For<IExecution>(Id.Owner, queue.ActiveExecutionName!);
        ExecutionSnapshot snapshot;
        try
        {
            snapshot = await GrainFactory.GetGrain<IExecution>(executionId.ToGrainId())
                .Read()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            turns[index] = record with
            {
                Status = ChatTurnStatus.Failed,
                Revision = record.Revision + 1,
            };
            SaveTurns(turns);
            queue = queue with { ActiveTurnId = null, ActiveExecutionName = null };
            SaveQueue(queue);
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await EmitAsync(new TurnLifecycle(
                new TurnId(record.TurnId),
                new CommandId(record.CommandId),
                Id,
                ChatTurnStatus.Failed,
                "waiting-deadline-unreadable")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (IsExecutionTerminal(snapshot.State))
        {
            await ApplyExecutionSnapshotToTurnAsync(turns, index, snapshot)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Owner policy: CancelExecution + bridge after park deadline (must not freeze FIFO).
        try
        {
            await GrainFactory.GetGrain<IExecution>(executionId.ToGrainId())
                .Apply(new ApplyExecution(
                    CommandId.New(),
                    new CancelExecution(),
                    ExpectedRevision: snapshot.Revision))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (NeuronAuthorizationException)
        {
            // Revision race — re-Read below.
        }

        try
        {
            snapshot = await GrainFactory.GetGrain<IExecution>(executionId.ToGrainId())
                .Read()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            return;
        }

        if (IsExecutionTerminal(snapshot.State))
        {
            await ApplyExecutionSnapshotToTurnAsync(turns, index, snapshot)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (record.Status != ChatTurnStatus.Cancelling)
        {
            turns = LoadTurns();
            index = turns.FindIndex(turn => turn.TurnId == record.TurnId);
            if (index >= 0)
            {
                turns[index] = turns[index] with
                {
                    Status = ChatTurnStatus.Cancelling,
                    Revision = turns[index].Revision + 1,
                };
                SaveTurns(turns);
                await EmitAsync(new TurnLifecycle(
                    new TurnId(record.TurnId),
                    new CommandId(record.CommandId),
                    Id,
                    ChatTurnStatus.Cancelling,
                    "waiting-deadline-cancel")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }

        // Keep the reminder so a still-parked head is retried until terminal.
        await this.RegisterOrUpdateReminder(
                WaitingDeadlineReminderName,
                WaitingPolicyDeadline,
                ReminderPeriod)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task UnregisterWaitingDeadlineAsync()
    {
        if (await this.GetReminder(WaitingDeadlineReminderName)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } reminder)
        {
            await this.UnregisterReminder(reminder)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private static int FindTurnIndexForExecution(
        List<DurableTurnRecord> turns,
        TurnQueueState queue,
        string executionName)
    {
        var index = turns.FindIndex(turn =>
            string.Equals(turn.ExecutionName, executionName, StringComparison.Ordinal));
        if (index < 0
            && queue.ActiveTurnId is { } activeId
            && string.Equals(queue.ActiveExecutionName, executionName, StringComparison.Ordinal))
        {
            index = turns.FindIndex(turn => turn.TurnId == activeId);
        }

        return index;
    }

    private async Task ApplyExecutionSnapshotToTurnAsync(
        List<DurableTurnRecord> turns,
        int index,
        ExecutionSnapshot snapshot)
    {
        var record = turns[index];
        var result = snapshot.Result;
        var failure = snapshot.Failure;

        // Idempotent re-apply by Execution revision: duplicate wake-ups change nothing.
        if (record.AppliedExecutionRevision is { } applied
            && applied == snapshot.Revision
            && record.Status is ChatTurnStatus.Completed
                or ChatTurnStatus.Failed
                or ChatTurnStatus.Cancelled)
        {
            var queueDone = LoadQueue();
            if (queueDone.ActiveTurnId == record.TurnId)
            {
                queueDone = queueDone with { ActiveTurnId = null, ActiveExecutionName = null };
                SaveQueue(queueDone);
                await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return;
        }

        var alreadyTerminal = record.Status is ChatTurnStatus.Completed
            or ChatTurnStatus.Failed
            or ChatTurnStatus.Cancelled;

        if (alreadyTerminal)
        {
            // Different kernel revision after terminal — do not re-emit Responded.
            var queueDone = LoadQueue();
            if (queueDone.ActiveTurnId == record.TurnId)
            {
                queueDone = queueDone with { ActiveTurnId = null, ActiveExecutionName = null };
                SaveQueue(queueDone);
            }

            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var status = snapshot.State switch
        {
            ExecutionState.Succeeded => ChatTurnStatus.Completed,
            ExecutionState.Failed => ChatTurnStatus.Failed,
            ExecutionState.Cancelled => ChatTurnStatus.Cancelled,
            _ => record.Status,
        };

        if (status is not (ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled))
        {
            return;
        }

        turns[index] = record with
        {
            Status = status,
            Revision = record.Revision + 1,
            AppliedExecutionRevision = snapshot.Revision,
        };
        SaveTurns(turns);
        if (record.Status == ChatTurnStatus.Waiting)
        {
            await UnregisterWaitingDeadlineAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (status == ChatTurnStatus.Completed)
        {
            await TryEmitRespondedAsync(record, result)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        var detail = status switch
        {
            ChatTurnStatus.Failed when failure is ChatTurnFailure chatFailure => chatFailure.Reason,
            ChatTurnStatus.Failed when failure is WorkerAbandoned abandoned => abandoned.Reason,
            ChatTurnStatus.Failed => failure?.GetType().Name,
            ChatTurnStatus.Cancelled => "cancelled",
            _ => record.ExecutionName,
        };

        await EmitAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            status,
            detail)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var queue = LoadQueue();
        if (queue.ActiveTurnId == record.TurnId)
        {
            queue = queue with { ActiveTurnId = null, ActiveExecutionName = null };
            SaveQueue(queue);
        }

        DelayDeactivation(TimeSpan.FromMinutes(1));
        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task TryEmitRespondedAsync(DurableTurnRecord record, Result? result)
    {
        if (result is not ChatTurnResult chatResult || string.IsNullOrWhiteSpace(chatResult.Answer))
        {
            return;
        }

        // Emit first: if Emit fails the delivery retries. Remembering before Emit left a
        // transcript line that made retries skip the Emit forever (no Responded in journal).
        await EmitAsync(new Responded(
            new CommandId(record.CommandId),
            Id,
            chatResult.Answer,
            Author: chatResult.Author ?? string.Empty))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var existing = Turns();
        if (!existing.Any(turn => !turn.FromUser
            && string.Equals(turn.Text, chatResult.Answer, StringComparison.Ordinal)))
        {
            Remember(new ChatTurn(FromUser: false, chatResult.Answer));
        }

        await SendAsync(
            ICorpus.ForOwner(Id.Owner),
            new AppendCorpusEntry(
                CommandId.New(),
                Kind: "chat.responded",
                Text: chatResult.Answer,
                Correlation: record.CommandId.ToString("n"),
                At: DateTimeOffset.UtcNow))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static bool IsExecutionTerminal(ExecutionState state)
        => state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;

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
            new StartExecution(goal, worker, TurnPolicy, RetryOf: null, Origin: Id)))
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
        // Stay activated for the duration of the AI attempt so idle reactivation
        // does not spuriously cancel a live head (see ReconcileActiveExecutionAsync).
        DelayDeactivation(TimeSpan.FromHours(2));

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

}
