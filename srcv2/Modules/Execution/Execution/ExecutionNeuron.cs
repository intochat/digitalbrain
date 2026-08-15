using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Execution;

[GrainType("execution")]
public sealed class ExecutionNeuron :
    Neuron,
    IExecution,
    IExecutionWorkerLease,
    IHandle<ApplyExecution>,
    IHandle<PrepareOperation>,
    IHandle<TransitionOperation>,
    IHandle<ReadOperation>,
    IHandle<UserActionRequired>,
    IHandle<CompleteUserAction>,
    IHandle<DenyUserAction>,
    IHandle<AttemptAccepted>,
    IHandle<AttemptProgressed>,
    IHandle<AttemptWaiting>,
    IHandle<AttemptSucceeded>,
    IHandle<AttemptFailed>,
    IHandle<AttemptCancelled>,
    IHandle<AttemptOutcomeUncertain>,
    IRemindable
{
    private const string StateName = "db.execution.state";
    private readonly ExecutionAttemptHandler _attempts;
    private readonly ExecutionCommandHandler _commands;
    private readonly ExecutionDispatcher _dispatcher;
    private readonly ExecutionOperationHandler _operations;
    private readonly ExecutionRuntime _runtime;
    private readonly ExecutionUserActionHandler _userActions;

    public ExecutionNeuron()
    {
        var state = new ExecutionStateStore(
            ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName),
            ServiceProvider.GetRequiredService<Serializer<ExecutionData>>());
        _runtime = new ExecutionRuntime(this, state);
        _dispatcher = new ExecutionDispatcher(_runtime);
        _commands = new ExecutionCommandHandler(_runtime, _dispatcher);
        _operations = new ExecutionOperationHandler(_runtime);
        _userActions = new ExecutionUserActionHandler(_runtime, _dispatcher);
        _attempts = new ExecutionAttemptHandler(_runtime, _dispatcher);
    }

    internal IGrainFactory ExecutionGrainFactory => GrainFactory;

    internal TimeProvider ExecutionTimeProvider => TimeProvider;

    public Task<ExecutionSnapshot> Read()
        => Task.FromResult(ExecutionModel.Snapshot(_runtime.Load()));

    public Task<ExecutionSnapshot> Apply(ApplyExecution command)
        => _commands.ApplyAsync(command);

    public Task RenewLease(AttemptCursor cursor)
        => _attempts.RenewLeaseAsync(cursor);

    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _dispatcher.RecoverAfterActivationAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ApplyExecution synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await _commands.ApplyAsync(synapse)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(snapshot, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ReadOperation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        var response = _operations.Read(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(response, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(PrepareOperation synapse, CancellationToken cancellationToken)
    {
        var response = await _operations.PrepareAsync(synapse, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(response, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(TransitionOperation synapse, CancellationToken cancellationToken)
    {
        var response = await _operations.TransitionAsync(synapse, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(response, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(UserActionRequired control, CancellationToken cancellationToken)
        => await _userActions.HandleRequiredAsync(control, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(CompleteUserAction command, CancellationToken cancellationToken)
        => await _userActions.CompleteAsync(command, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(DenyUserAction command, CancellationToken cancellationToken)
        => await _userActions.DenyAsync(command, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(AttemptAccepted fact, CancellationToken cancellationToken)
        => await _attempts.AcceptedAsync(fact)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(AttemptProgressed fact, CancellationToken cancellationToken)
        => await _attempts.ProgressedAsync(fact)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(AttemptWaiting fact, CancellationToken cancellationToken)
        => await _attempts.WaitingAsync(fact)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(AttemptSucceeded fact, CancellationToken cancellationToken)
        => await _attempts.SucceededAsync(fact)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(AttemptFailed fact, CancellationToken cancellationToken)
        => await _attempts.FailedAsync(fact)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(AttemptCancelled fact, CancellationToken cancellationToken)
        => await _attempts.CancelledAsync(fact)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    public async Task HandleAsync(
        AttemptOutcomeUncertain fact,
        CancellationToken cancellationToken)
        => await _attempts.OutcomeUncertainAsync(fact)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
        => await _dispatcher.ReceiveReminderAsync(reminderName)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    internal void EnlistExecutionRollback(Action rollback)
        => EnlistTurnRollback(rollback);

    internal ValueTask WriteExecutionStateAsync()
        => WriteStateAsync();

    internal Task<IGrainReminder> RegisterExecutionReminderAsync(
        string name,
        TimeSpan dueTime,
        TimeSpan period)
        => this.RegisterOrUpdateReminder(name, dueTime, period);

    internal async Task UnregisterExecutionReminderAsync(string name)
    {
        if (await this.GetReminder(name)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } reminder)
        {
            await this.UnregisterReminder(reminder)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    internal Task<SynapseDelivery> SendFromExecutionAsync(NeuronId receiver, Synapse synapse)
        => SendAsync(receiver, synapse);

    internal Task EmitFromExecutionAsync(Synapse synapse)
        => EmitAsync(synapse);

    internal void DelayExecutionDeactivation(TimeSpan duration)
        => DelayDeactivation(duration);

    private bool ShouldDeliver(SynapseDelivery delivery)
        => ExecutionDeliveryAuthorizer.ShouldDeliver(
            delivery,
            Id,
            _runtime.LoadIfStarted);

    Task INeuron.Deliver(SynapseDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        return ShouldDeliver(delivery)
            ? base.Deliver(delivery, cancellationToken)
            : Task.CompletedTask;
    }
}
