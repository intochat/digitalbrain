namespace Brain.Gateway;

public interface ILiveFeedSubscription
{
    Task SubscribeAsync(Func<FeedEvent, Task> onEvent, CancellationToken cancellationToken = default);
}
