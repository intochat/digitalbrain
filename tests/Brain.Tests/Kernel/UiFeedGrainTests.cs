using Brain.Contracts;
using Brain.Kernel;
using Orleans.Streams;
using Xunit;

namespace Brain.Tests.Kernel;

public sealed class UiFeedGrainTests : IClassFixture<ReactiveNeuronClusterFixture>
{
    private readonly ReactiveNeuronClusterFixture _fixture;

    public UiFeedGrainTests(ReactiveNeuronClusterFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Candidate_is_journaled_with_a_positive_global_sequence_before_live_delivery()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new OrganizationId($"org-{suffix}");
        var space = new SpaceId($"space-{suffix}");
        var feed = Feed(organization, space);
        var driver = Driver(suffix);
        await driver.SubscribeLiveAsync(organization, space);
        await feed.EnsureSubscribedAsync();

        var @event = SnapshotEvent(
            organization,
            space,
            Guid.NewGuid(),
            new UiSurface($"surface-{suffix}", 1, [new UiBlock("text", "ready", [])]));
        await driver.PublishCandidateAsync(@event);

        var page = await WaitForPageAsync(feed, expectedCount: 1);
        var frame = Assert.Single(page.Frames);
        Assert.Equal(UiFeedFrame.CurrentSchemaVersion, frame.SchemaVersion);
        Assert.Equal(1, frame.Sequence);
        Assert.Equal(UiFeedFrameTypes.Snapshot, frame.Type);
        Assert.Equal(@event.Metadata.EventId, frame.EventId);
        Assert.Equal("ready", frame.Snapshot!.Surface.Blocks[0].Text);

        var delivered = await WaitForLiveAsync(driver, expectedCount: 1);
        var live = Assert.Single(delivered);
        Assert.Equal(frame.SchemaVersion, live.SchemaVersion);
        Assert.Equal(frame.Sequence, live.Sequence);
        Assert.Equal(frame.EventId, live.EventId);
        Assert.Equal(frame.Type, live.Type);
        Assert.Equal(frame.Snapshot.Surface.SurfaceId, live.Snapshot!.Surface.SurfaceId);
        Assert.Equal(frame.Snapshot.Surface.Revision, live.Snapshot.Surface.Revision);
    }

    [Fact]
    public async Task Sequences_are_contiguous_across_surfaces_and_duplicate_event_ids_are_ignored()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new OrganizationId($"org-{suffix}");
        var space = new SpaceId($"space-{suffix}");
        var feed = Feed(organization, space);
        var driver = Driver(suffix);
        await feed.EnsureSubscribedAsync();

        var first = SnapshotEvent(
            organization,
            space,
            Guid.NewGuid(),
            new UiSurface("surface-a", 1, [new UiBlock("topic", "A", [])]));
        var second = SnapshotEvent(
            organization,
            space,
            Guid.NewGuid(),
            new UiSurface("surface-b", 4, [new UiBlock("status", "B", [])]));

        await driver.PublishCandidateAsync(first);
        await driver.PublishCandidateAsync(second);
        await driver.PublishCandidateAsync(first);

        var page = await WaitForPageAsync(feed, expectedCount: 2);
        Assert.Equal([1L, 2L], page.Frames.Select(frame => frame.Sequence));
        Assert.Equal(
            [first.Metadata.EventId, second.Metadata.EventId],
            page.Frames.Select(frame => frame.EventId));
        Assert.Equal(2, page.NextCursor);
    }

    [Fact]
    public async Task Paging_resumes_after_the_cursor_and_is_bounded()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new OrganizationId($"org-{suffix}");
        var space = new SpaceId($"space-{suffix}");
        var feed = Feed(organization, space);
        var driver = Driver(suffix);
        await feed.EnsureSubscribedAsync();

        for (var revision = 1; revision <= 3; revision++)
        {
            await driver.PublishCandidateAsync(SnapshotEvent(
                organization,
                space,
                Guid.NewGuid(),
                new UiSurface("surface", revision, [new UiBlock("text", revision.ToString(), [])])));
        }

        await WaitForPageAsync(feed, expectedCount: 3);
        var page = await feed.ReadAsync(cursor: 1, max: 1);

        Assert.Equal(2, Assert.Single(page.Frames).Sequence);
        Assert.Equal(2, page.NextCursor);
    }

    [Fact]
    public async Task Candidate_from_another_identity_is_rejected_without_journaling()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new OrganizationId($"org-{suffix}");
        var space = new SpaceId($"space-{suffix}");
        var feed = Feed(organization, space);
        var driver = Driver(suffix);
        await feed.EnsureSubscribedAsync();

        var rejected = SnapshotEvent(
            new OrganizationId("other-org"),
            space,
            Guid.NewGuid(),
            new UiSurface("surface", 1, [new UiBlock("failure", "secret raw detail", [])]));

        await driver.PublishCandidateToAsync(organization, space, rejected);
        await Task.Delay(100);
        var page = await feed.ReadAsync(0, 10);
        Assert.Empty(page.Frames);
        Assert.Equal(0, page.NextCursor);
    }

    private IUiFeed Feed(OrganizationId organization, SpaceId space) =>
        _fixture.Cluster.GrainFactory.GetGrain<IUiFeed>(UiFeedStreams.FeedKey(organization, space));

    private IUiFeedTestDriver Driver(string suffix) =>
        _fixture.Cluster.GrainFactory.GetGrain<IUiFeedTestDriver>($"driver-{suffix}");

    private static EventSynapse<UiFeedCandidate> SnapshotEvent(
        OrganizationId organization,
        SpaceId space,
        Guid eventId,
        UiSurface surface)
    {
        var source = new NeuronAddress(organization, space, "test.source.v1", surface.SurfaceId);
        var metadata = new SynapseMetadata(
            CommandId: eventId,
            EventId: eventId,
            CausationId: eventId,
            CorrelationId: eventId,
            OrganizationId: organization,
            PrincipalId: new PrincipalId("test"),
            SpaceId: space,
            Source: source,
            SourceSequence: 1,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);
        return new EventSynapse<UiFeedCandidate>(
            metadata,
            UiFeedCandidate.CreateSnapshot(new UiSurfaceSnapshot(surface)));
    }

    private static async Task<UiFeedPage> WaitForPageAsync(IUiFeed feed, int expectedCount)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var page = await feed.ReadAsync(0, 100);
            if (page.Frames.Count >= expectedCount)
                return page;
            await Task.Delay(25);
        }

        throw new TimeoutException($"UI feed did not reach {expectedCount} frames.");
    }

    private static async Task<IReadOnlyList<UiFeedFrame>> WaitForLiveAsync(
        IUiFeedTestDriver driver,
        int expectedCount)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var frames = await driver.GetLiveFramesAsync();
            if (frames.Count >= expectedCount)
                return frames;
            await Task.Delay(25);
        }

        throw new TimeoutException($"Live UI feed did not reach {expectedCount} frames.");
    }
}

[Alias("brain.tests.IUiFeedTestDriver")]
public interface IUiFeedTestDriver : IGrainWithStringKey
{
    [Alias("SubscribeLiveAsync")]
    Task SubscribeLiveAsync(OrganizationId organization, SpaceId space);

    [Alias("PublishCandidateAsync")]
    Task PublishCandidateAsync(EventSynapse<UiFeedCandidate> candidate);

    [Alias("PublishCandidateToAsync")]
    Task PublishCandidateToAsync(
        OrganizationId organization,
        SpaceId space,
        EventSynapse<UiFeedCandidate> candidate);

    [Alias("GetLiveFramesAsync")]
    Task<IReadOnlyList<UiFeedFrame>> GetLiveFramesAsync();
}

public sealed class UiFeedTestDriverGrain : Grain, IUiFeedTestDriver
{
    private readonly List<UiFeedFrame> _frames = [];
    private StreamSubscriptionHandle<UiFeedFrame>? _subscription;

    public async Task SubscribeLiveAsync(OrganizationId organization, SpaceId space)
    {
        if (_subscription is not null)
            return;

        var stream = Stream<UiFeedFrame>(
            UiFeedStreams.LiveNamespace,
            UiFeedStreams.StreamId(organization, space));
        _subscription = await stream.SubscribeAsync((frame, _) =>
        {
            _frames.Add(frame);
            return Task.CompletedTask;
        });
    }

    public Task PublishCandidateAsync(EventSynapse<UiFeedCandidate> candidate) =>
        PublishCandidateToAsync(
            candidate.Metadata.OrganizationId,
            candidate.Metadata.SpaceId,
            candidate);

    public Task PublishCandidateToAsync(
        OrganizationId organization,
        SpaceId space,
        EventSynapse<UiFeedCandidate> candidate) =>
        Stream<EventSynapse<UiFeedCandidate>>(
                UiFeedStreams.CandidateNamespace,
                UiFeedStreams.StreamId(organization, space))
            .OnNextAsync(candidate);

    public Task<IReadOnlyList<UiFeedFrame>> GetLiveFramesAsync() =>
        Task.FromResult<IReadOnlyList<UiFeedFrame>>([.. _frames]);

    private IAsyncStream<T> Stream<T>(string streamNamespace, Guid streamId) =>
        this.GetStreamProvider(ReactiveNeuron<UiFeedCandidate>.DefaultStreamProviderName)
            .GetStream<T>(StreamId.Create(streamNamespace, streamId));
}
