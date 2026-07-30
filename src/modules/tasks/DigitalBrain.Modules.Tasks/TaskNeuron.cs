using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Tasks;

[GrainType("task")]
internal sealed partial class TaskNeuron :
    Neuron,
    ITask,
    IHandle<AttemptAccepted>,
    IHandle<AttemptProgressed>,
    IHandle<AttemptWaiting>,
    IHandle<AttemptSucceeded>,
    IHandle<AttemptFailed>,
    IHandle<AttemptCancelled>,
    IHandle<AttemptOutcomeUncertain>,
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

    public Task<TaskSnapshot> Read()
    {
        var data = LoadIfStarted();
        return Task.FromResult(data is null
            ? new TaskSnapshot(
                default!,
                default,
                default!,
                TaskState.Pending,
                Revision: 0,
                ActiveAttempt: null,
                Blocker: null,
                Result: null,
                Failure: null,
                Evidence: [],
                RetryOf: null,
                AttemptCount: 0,
                Activation: null)
            : Snapshot(data));
    }

    Task INeuron.Deliver(SynapseDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return delivery.Synapse is AttemptFact fact && delivery.Caller != fact.Worker
            ? Task.CompletedTask
            : base.Deliver(delivery);
    }
}
