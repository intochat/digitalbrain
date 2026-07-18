using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Salesforce;
using Orleans.Streams;

namespace Brain.Tests.Salesforce;

[Alias("brain.tests.salesforce.ISalesforceFeedObserver")]
public interface ISalesforceFeedObserver : IGrainWithGuidKey
{
    [Alias("ReadyAsync")]
    Task ReadyAsync(string streamNamespace, Guid streamId);

    [Alias("GetEventsAsync")]
    Task<IReadOnlyList<SalesforceFeedEvent>> GetEventsAsync();

    [Alias("ClearAsync")]
    Task ClearAsync();
}

public sealed class SalesforceFeedObserverGrain : Grain, ISalesforceFeedObserver
{
    private readonly List<SalesforceFeedEvent> _events = [];
    private StreamSubscriptionHandle<EventSynapse<SalesforceFeedEvent>>? _handle;

    public async Task ReadyAsync(string streamNamespace, Guid streamId)
    {
        if (_handle is not null)
            return;

        var provider = this.GetStreamProvider(ReactiveNeuron<SalesforceFeedEvent>.DefaultStreamProviderName);
        var stream = provider.GetStream<EventSynapse<SalesforceFeedEvent>>(StreamId.Create(streamNamespace, streamId));
        var existing = await stream.GetAllSubscriptionHandles();
        if (existing.Count > 0)
        {
            foreach (var handle in existing)
                _handle = await handle.ResumeAsync(OnNextAsync);
            return;
        }

        _handle = await stream.SubscribeAsync(OnNextAsync);
    }

    public Task<IReadOnlyList<SalesforceFeedEvent>> GetEventsAsync() =>
        Task.FromResult<IReadOnlyList<SalesforceFeedEvent>>(_events.ToArray());

    public Task ClearAsync()
    {
        _events.Clear();
        return Task.CompletedTask;
    }

    private Task OnNextAsync(EventSynapse<SalesforceFeedEvent> item, StreamSequenceToken? token)
    {
        _events.Add(item.Payload);
        return Task.CompletedTask;
    }
}
