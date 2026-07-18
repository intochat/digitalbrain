using DigitalBrain.Runtime.Neurons;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Kernel.Visualization;

public interface ITaskManagerObserverGrain : IGrainWithGuidKey
{
    Task EnsureActivatedAsync();
}

// Forwards every global-timeline synapse onto the singleton TaskManagerNeuron.
// Lives only in the kernel project, so Orleans places it on the kernel silo.
// Cluster-singleton (Guid.Empty key), matching TimelineRelayGrain. The
// feedback-loop guard drops RfwCards the TaskManagerNeuron itself emitted so
// the projection does not count its own broadcasts as activity.
[ImplicitStreamSubscription(Neuron.GlobalTimelineNamespace)]
public sealed class TaskManagerObserverGrain(
    IGrainFactory grains,
    ILogger<TaskManagerObserverGrain> logger)
    : Grain, ITaskManagerObserverGrain, IStreamSubscriptionObserver, IAsyncObserver<Synapse>
{
    public Task EnsureActivatedAsync() => Task.CompletedTask;

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var handle = handleFactory.Create<Synapse>();
        try
        {
            await handle.ResumeAsync(this);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resuming subscription with cached token failed in TaskManagerObserverGrain. Falling back to fresh subscribe.");
            await handle.ResumeAsync(this, null);
        }
    }

    public async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        if (string.Equals(
                item.CallerNeuronType,
                TaskManagerNeuron.TaskManagerNeuronType,
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var neuron = grains.GetGrain<ITaskManagerNeuron>(Guid.Empty);
            await neuron.Observe(item);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "TaskManager observer failed to forward synapse {SynapseType} (correlation {CorrelationId}).",
                item.GetType().Name, item.CorrelationId);
        }
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        if (ex is QueueCacheMissException || ex.GetType().FullName == "Orleans.Streams.QueueCacheMissException")
        {
            logger.LogWarning(ex, "Transient stream cache miss in TaskManagerObserverGrain; Orleans pulling agent will recover.");
        }
        else
        {
            logger.LogError(ex, "TaskManager observer stream subscription error.");
        }
        return Task.CompletedTask;
    }
}
