using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Execution;

[GrainType("execution")]
public sealed partial class ExecutionNeuron :
    Neuron,
    IExecution,
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
    private const string RetryReminderName = "db.execution.retry";
    private const string DispatchReminderName = "db.execution.dispatch";
    internal const int RememberedReceipts = 64;
    internal const int RememberedOperations = 64;
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<ExecutionData> _states;

    public ExecutionNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<ExecutionData>>();
    }

    public Task<ExecutionSnapshot> Read() => Task.FromResult(Snapshot(Load()));

    public async Task HandleAsync(ApplyExecution synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await Apply(synapse).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    Task INeuron.Deliver(SynapseDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        if (delivery.Synapse is AttemptFact fact && delivery.Caller != fact.Worker)
        {
            return Task.CompletedTask;
        }

        if (delivery.Synapse is UserActionRequired)
        {
            var data = LoadIfStarted();
            if (data is null || delivery.Caller != data.Worker)
            {
                return Task.CompletedTask;
            }
        }

        if (delivery.Synapse is CompleteUserAction or DenyUserAction)
        {
            var data = LoadIfStarted();
            if (data is null)
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not the user-action completer for Execution '{Id}'.");
            }

            if (data.Blocker is not UserActionPending pending)
            {
                var expectedRevision = delivery.Synapse switch
                {
                    CompleteUserAction complete => complete.ExpectedParkRevision,
                    DenyUserAction deny => deny.ExpectedParkRevision,
                    _ => -1L,
                };

                if (data.State is ExecutionState.Running or ExecutionState.Pending
                    && expectedRevision >= 0
                    && data.Revision == expectedRevision)
                {
                    throw new InvalidOperationException(
                        $"Execution '{Id}' is not waiting on a module user action yet.");
                }

                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not the user-action completer for Execution '{Id}'.");
            }

            if (delivery.Caller != pending.Completer)
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not the user-action completer for Execution '{Id}'.");
            }
        }

        if (delivery.Synapse is PrepareOperation or TransitionOperation)
        {
            var data = LoadIfStarted();
            if (data is null || delivery.Caller != data.Worker)
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not authorized to submit operations for Execution '{Id}'.");
            }
        }
        else if (delivery.Synapse is ReadOperation)
        {
            var data = LoadIfStarted();
            if (data is null || !IsAuthorizedOperationReader(delivery.Caller, data.Worker))
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not authorized to read operations for Execution '{Id}'.");
            }
        }

        return base.Deliver(delivery, cancellationToken);
    }

    private bool IsAuthorizedOperationReader(NeuronId caller, NeuronId worker)
        => caller == worker
            || (caller.Owner == Id.Owner
                && string.Equals(caller.Type, ISessionNeuron.GrainTypeName, StringComparison.OrdinalIgnoreCase));
}
