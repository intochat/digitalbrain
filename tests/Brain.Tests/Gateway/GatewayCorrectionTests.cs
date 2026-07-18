using Brain.Contracts;
using Brain.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brain.Tests.Gateway;

public sealed class GatewayCorrectionTests
{
    [Fact]
    public void Gateway_registers_explicit_production_di_and_orleans_client()
    {
        var hosting = File.ReadAllText(Path.Combine(SourceRoot("Brain.Gateway"), "GatewayHosting.cs"));
        Assert.Contains("UseOrleansClient", hosting, StringComparison.Ordinal);
        Assert.Contains("ISurfaceOwnerResolver", hosting, StringComparison.Ordinal);
        Assert.Contains(nameof(OrleansDurableFeed), hosting, StringComparison.Ordinal);
        Assert.Contains("ILiveFeedSubscriptionFactory", hosting, StringComparison.Ordinal);
        Assert.DoesNotContain("InMemory", hosting, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(SourceRoot("Brain.Gateway"), "Program.cs"));
        Assert.Contains("AddGatewayServices", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ISurfaceOwner surfaceOwner", program, StringComparison.Ordinal);
        Assert.Contains("ISurfaceOwnerResolver", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_surface_owner_resolver_resolves_known_contracts_and_rejects_unknown()
    {
        var resolver = new TypedSurfaceOwnerResolver(new ThrowingNeuronLookup());
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("unknown.contract.v1", "x"));
        Assert.Contains("unknown", ex.Message, StringComparison.OrdinalIgnoreCase);

        var resolverSource = File.ReadAllText(Path.Combine(SourceRoot("Brain.Gateway"), "TypedSurfaceOwnerResolver.cs"));
        Assert.Contains("KnownSurfaceContracts.GroupChat", resolverSource, StringComparison.Ordinal);
        Assert.Contains("KnownSurfaceContracts.Gmail", resolverSource, StringComparison.Ordinal);
        Assert.Contains("KnownSurfaceContracts.Salesforce", resolverSource, StringComparison.Ordinal);
        Assert.Contains("GroupChatSurfaceOwner", resolverSource, StringComparison.Ordinal);
        Assert.Contains("GmailSurfaceOwner", resolverSource, StringComparison.Ordinal);
        Assert.Contains("SalesforceSurfaceOwner", resolverSource, StringComparison.Ordinal);
        Assert.Contains("IClusterClient", resolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAssemblies", resolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AppDomain", resolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchProxy", resolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", resolverSource, StringComparison.Ordinal);

        var lookupSource = File.ReadAllText(Path.Combine(SourceRoot("Brain.Gateway"), "ITypedNeuronLookup.cs"));
        Assert.Contains("Brain.Client.Brain", lookupSource, StringComparison.Ordinal);
        Assert.Contains("IClusterClient", lookupSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Orleans_durable_feed_pages_after_cursor_via_typed_grain()
    {
        var grain = new RecordingUiFeedGrain();
        var feed = new OrleansDurableFeed(new FixedUiFeedGrainAccessor(grain));
        grain.Pages[1] =
        [
            new FeedEvent(Guid.NewGuid(), "group-chat", 2, new UiSurfacePatch("group-chat", 1, 2, [])),
            new FeedEvent(Guid.NewGuid(), "group-chat", 3, new UiSurfacePatch("group-chat", 2, 3, []))
        ];

        var page = await feed.ReadPageAsync(afterRevision: 1, pageSize: 10);

        Assert.Equal(1L, grain.LastAfterRevision);
        Assert.Equal(10, grain.LastPageSize);
        Assert.Equal(2, page.Count);
        Assert.Equal(Brain.Gateway.IUiFeed.FeedContractId, grain.RequestedKey.Split('|')[2].Split('/')[0]);
    }

    [Fact]
    public async Task Reconnect_ignores_stale_replay_and_never_regresses_cursor()
    {
        var stale = new FeedEvent(Guid.NewGuid(), "group-chat", 1, Patch(0, 1));
        var current = new FeedEvent(Guid.NewGuid(), "group-chat", 3, Patch(2, 3));
        var live = new BufferingLiveFeed([stale, current]);
        var durable = new StaticDurableFeed([stale]);
        var owner = new RecordingSurfaceOwner();
        await using var session = new UiFeedSession(live, durable, owner, lastKnownRevision: 2);

        var result = await session.ReconnectAsync();

        Assert.Single(result.Events);
        Assert.Equal(current.EventId, result.Events[0].EventId);
        Assert.Equal(3, result.Cursor);
        Assert.True(result.Cursor >= 2);
    }

    [Fact]
    public async Task Reconnect_disposes_live_subscription()
    {
        var live = new DisposableLiveFeed();
        var durable = new StaticDurableFeed([]);
        var owner = new RecordingSurfaceOwner();
        var session = new UiFeedSession(live, durable, owner, lastKnownRevision: 0);

        await session.ReconnectAsync();
        Assert.False(live.Disposed);
        await session.DisposeAsync();
        Assert.True(live.Disposed);
    }

    [Fact]
    public void Orleans_live_feed_subscription_retains_and_disposes_handle()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot("Brain.Gateway"), "OrleansLiveFeedSubscription.cs"));
        Assert.Contains("StreamSubscriptionHandle", source, StringComparison.Ordinal);
        Assert.Contains("UnsubscribeAsync", source, StringComparison.Ordinal);
        Assert.Contains("DisposeAsync", source, StringComparison.Ordinal);
        Assert.Contains(": ILiveFeedSubscription", source, StringComparison.Ordinal);

        var liveInterface = File.ReadAllText(Path.Combine(SourceRoot("Brain.Gateway"), "ILiveFeedSubscription.cs"));
        Assert.Contains("IAsyncDisposable", liveInterface, StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway_feed_configuration_fails_closed_when_missing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        Assert.ThrowsAny<Exception>(() => GatewayHosting.AddGatewayApplicationServices(services, configuration));
    }

    private static string SourceRoot(string project) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", project));

    private static UiSurfacePatch Patch(long from, long to) =>
        new("group-chat", from, to, [new UiPatchOperation("replace", "/blocks/0/text", "x")]);
}

internal sealed class DisposableLiveFeed : ILiveFeedSubscription
{
    public bool Disposed { get; private set; }

    public Task SubscribeAsync(Func<FeedEvent, Task> onEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingUiFeedGrain : Brain.Gateway.IUiFeed
{
    public Dictionary<long, IReadOnlyList<FeedEvent>> Pages { get; } = new();
    public long LastAfterRevision { get; private set; }
    public int LastPageSize { get; private set; }
    public string RequestedKey { get; set; } = Brain.Gateway.IUiFeed.CreateGrainKey(
        DevelopmentPrincipal.Current.OrganizationId,
        DevelopmentPrincipal.Current.SpaceId);

    public Task<Brain.Gateway.UiFeedPage> ReadPageAsync(long afterRevision, int pageSize)
    {
        LastAfterRevision = afterRevision;
        LastPageSize = pageSize;
        var events = Pages.TryGetValue(afterRevision, out var page) ? page : Array.Empty<FeedEvent>();
        var next = events.Count == 0 ? afterRevision : events[^1].Revision;
        return Task.FromResult(new Brain.Gateway.UiFeedPage(events, next));
    }
}

internal sealed class FixedUiFeedGrainAccessor(Brain.Gateway.IUiFeed grain) : IUiFeedGrainAccessor
{
    public Brain.Gateway.IUiFeed GetFeed(OrganizationId organizationId, SpaceId spaceId)
    {
        if (grain is RecordingUiFeedGrain recording)
            recording.RequestedKey = Brain.Gateway.IUiFeed.CreateGrainKey(organizationId, spaceId);
        return grain;
    }
}

internal sealed class ThrowingNeuronLookup : ITypedNeuronLookup
{
    public DigitalBrain.AI.IGroupChat GetGroupChat(string instanceId) => throw new NotSupportedException();
    public DigitalBrain.Google.IGmail GetGmail(string instanceId) => throw new NotSupportedException();
    public DigitalBrain.Salesforce.ISalesforce GetSalesforce(string instanceId) => throw new NotSupportedException();
}
