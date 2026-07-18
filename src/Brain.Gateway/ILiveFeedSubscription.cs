namespace Brain.Gateway;

public interface ILiveFeedSubscription : IAsyncDisposable
{
    Task SubscribeAsync(Func<FeedEvent, Task> onEvent, CancellationToken cancellationToken = default);
}

public interface ILiveFeedSubscriptionFactory
{
    ILiveFeedSubscription Create();
}
