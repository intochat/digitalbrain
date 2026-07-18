using Brain.Contracts;

namespace Brain.Gateway;

[Alias("digitalbrain.feed.IUiFeed")]
public interface IUiFeed : IGrainWithStringKey
{
    const string FeedContractId = "ui.feed.v1";
    const string FeedInstanceId = "main";

    [Alias("ReadPageAsync")]
    Task<UiFeedPage> ReadPageAsync(long afterRevision, int pageSize);

    static string CreateGrainKey(OrganizationId organizationId, SpaceId spaceId) =>
        new NeuronAddress(organizationId, spaceId, FeedContractId, FeedInstanceId).ToGrainKey();
}



[GenerateSerializer, Alias("brain.gateway.ui-feed-page.v1")]
public sealed record UiFeedPage(
    [property: Id(0)] IReadOnlyList<FeedEvent> Events,
    [property: Id(1)] long NextCursor);
