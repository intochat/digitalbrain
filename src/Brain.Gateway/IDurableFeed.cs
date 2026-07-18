namespace Brain.Gateway;

public interface IDurableFeed
{
    Task<IReadOnlyList<FeedEvent>> ReadPageAsync(long afterRevision, int pageSize, CancellationToken cancellationToken = default);
}
