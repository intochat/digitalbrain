using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Tasks;

[GrainType("task")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from GrainType metadata.")]
internal sealed partial class TaskNeuron :
    Neuron,
    ITask,
    IHandle<StartTask>,
    IHandle<PrepareTaskOperation>,
    IHandle<TransitionTaskOperation>,
    IHandle<ReadTaskOperation>,
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
    IEmit<TaskSnapshot>,
    IRemindable
{
    private const string StateName = "tasks.task";
    private const string RetryReminderName = "tasks.retry";
    private const string DispatchReminderName = "tasks.dispatch";
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<TaskData> _states;

    public TaskNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<TaskData>>();
    }

    public Task<TaskSnapshot> Read() => Task.FromResult(Snapshot(Load()));

    public async Task HandleAsync(StartTask synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await Start(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(snapshot, cancellationToken);
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
                    $"Caller '{delivery.Caller}' is not the user-action completer for Task '{Id}'.");
            }

            if (data.Blocker is not UserActionPending pending)
            {
                var expectedRevision = delivery.Synapse switch
                {
                    CompleteUserAction complete => complete.ExpectedParkRevision,
                    DenyUserAction deny => deny.ExpectedParkRevision,
                    _ => -1L,
                };

                if (data.State is TaskState.Running or TaskState.Pending
                    && expectedRevision >= 0
                    && data.Revision == expectedRevision)
                {
                    throw new InvalidOperationException(
                        $"Task '{Id}' is not waiting on a module user action yet.");
                }

                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not the user-action completer for Task '{Id}'.");
            }

            if (delivery.Caller != pending.Completer)
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not the user-action completer for Task '{Id}'.");
            }
        }

        if (delivery.Synapse is PrepareTaskOperation or TransitionTaskOperation)
        {
            var data = LoadIfStarted();
            if (data is null || delivery.Caller != data.Worker)
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not authorized to submit task operations for Task '{Id}'.");
            }
        }
        else if (delivery.Synapse is ReadTaskOperation)
        {
            var data = LoadIfStarted();
            if (data is null || !IsAuthorizedOperationReader(delivery.Caller, data.Worker))
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not authorized to read task operations for Task '{Id}'.");
            }
        }

        return base.Deliver(delivery, cancellationToken);
    }

    private bool IsAuthorizedOperationReader(NeuronId caller, NeuronId worker)
        => caller == worker
            || (caller.Owner == Id.Owner
                && string.Equals(caller.Type, ISessionNeuron.GrainTypeName, StringComparison.OrdinalIgnoreCase));
}
