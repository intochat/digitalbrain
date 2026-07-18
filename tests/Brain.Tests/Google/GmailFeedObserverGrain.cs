using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Google;
using Orleans.Streams;

namespace Brain.Tests.Google;

[Alias("brain.tests.google.IGmailFeedObserver")]
public interface IGmailFeedObserver : IGrainWithGuidKey
{
    [Alias("ReadyAsync")]
    Task ReadyAsync(string streamNamespace, Guid streamId);

    [Alias("GetEventsAsync")]
    Task<IReadOnlyList<GmailFeedEvent>> GetEventsAsync();

    [Alias("ClearAsync")]
    Task ClearAsync();
}

public sealed class GmailFeedObserverGrain : Grain, IGmailFeedObserver
{
    private readonly List<GmailFeedEvent> _events = [];
    private StreamSubscriptionHandle<EventSynapse<GmailFeedEvent>>? _handle;

    public async Task ReadyAsync(string streamNamespace, Guid streamId)
    {
        var provider = this.GetStreamProvider(ReactiveNeuron<GmailFeedEvent>.DefaultStreamProviderName);
        var stream = provider.GetStream<EventSynapse<GmailFeedEvent>>(StreamId.Create(streamNamespace, streamId));
        if (_handle is not null)
            return;

        var existing = await stream.GetAllSubscriptionHandles();
        if (existing.Count > 0)
        {
            foreach (var handle in existing)
                _handle = await handle.ResumeAsync(OnNextAsync);
            if (_handle is not null)
                return;
        }

        _handle = await stream.SubscribeAsync(OnNextAsync);
    }

    public Task<IReadOnlyList<GmailFeedEvent>> GetEventsAsync() =>
        Task.FromResult<IReadOnlyList<GmailFeedEvent>>(_events.ToArray());

    public Task ClearAsync()
    {
        _events.Clear();
        return Task.CompletedTask;
    }

    private Task OnNextAsync(EventSynapse<GmailFeedEvent> item, StreamSequenceToken? token)
    {
        _events.Add(item.Payload);
        return Task.CompletedTask;
    }
}
