namespace Brain.Gateway;

public sealed class OrleansDurableFeed(IUiFeedGrainAccessor feedAccessor) : IDurableFeed
{
    public async Task<IReadOnlyList<FeedEvent>> ReadPageAsync(long afterRevision, int pageSize, CancellationToken cancellationToken = default)
    {
        var feed = feedAccessor.GetFeed(DevelopmentPrincipal.OrganizationId, DevelopmentPrincipal.SpaceId);
        var page = await feed.ReadPageAsync(afterRevision, pageSize);
        return page.Events;
    }
}
