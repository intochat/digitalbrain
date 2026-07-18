using DigitalBrain.Runtime.History;
using DigitalBrain.Runtime.Neurons;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Runtime.Catalog;

[ImplicitStreamSubscription(Neuron.GlobalTimelineNamespace)]
public class BrainHistorianGrain(
    IGrainFactory grains,
    ILogger<BrainHistorianGrain> log)
    : Grain, IGrainWithGuidKey, IStreamSubscriptionObserver, IAsyncObserver<Synapse>
{
    public static readonly Guid Singleton = Guid.Empty;

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory factory)
    {
        var handle = factory.Create<Synapse>();
        try
        {
            await handle.ResumeAsync(this);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Resuming subscription with cached token failed in BrainHistorianGrain. Falling back to fresh subscribe.");
            await handle.ResumeAsync(this, null);
        }
    }

    public virtual async Task OnNextAsync(Synapse s, StreamSequenceToken? token = null)
    {
        var chain = grains.GetGrain<ICorrelationChain>(s.CorrelationId);
        await chain.AppendAsync(s, CancellationToken.None);

        var day = grains.GetGrain<IDayIndex>(s.Timestamp.UtcDateTime.ToString("yyyy-MM-dd"));
        await day.EnsureCorrelationAsync(s.CorrelationId, CancellationToken.None);
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        if (ex is QueueCacheMissException || ex.GetType().FullName == "Orleans.Streams.QueueCacheMissException")
        {
            log.LogWarning(ex, "Transient stream cache miss in BrainHistorianGrain; Orleans pulling agent will recover.");
        }
        else
        {
            log.LogError(ex, "BrainHistorianGrain stream subscription error");
        }
        return Task.CompletedTask;
    }
}
