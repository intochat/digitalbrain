using DigitalBrain.Qdrant;
using DigitalBrain.Qdrant.Query;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

public sealed class QdrantFacts
{
    private static readonly float[] Vector = [1f, 0f, 0f];
    private const string PointId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task UpsertSearchAndDeleteRoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FakeQdrantProvider(), ct);
        var qdrant = brain.Get<IQdrant>(QdrantNames.DefaultNeuron);

        var empty = await qdrant.ReadCollection(ct);
        Assert.False(empty.Exists);
        Assert.Equal(3, empty.VectorSize);

        var stored = await qdrant.Upsert(new([new QdrantPoint(PointId, Vector, [new QdrantField("topic", "greeting")])]), ct);
        Assert.Equal(1, stored.Stored);

        var found = await qdrant.Search(new(Vector, 5, [new QdrantField("topic", "greeting")]), ct);
        var match = Assert.Single(found.Matches);
        Assert.Equal(PointId, match.Id);
        Assert.Equal("greeting", Assert.Single(match.Payload).Value);

        var missed = await qdrant.Search(new(Vector, 5, [new QdrantField("topic", "other")]), ct);
        Assert.Empty(missed.Matches);

        var deleted = await qdrant.Delete(new([PointId]), ct);
        Assert.Equal(1, deleted.Accepted);
        Assert.Empty((await qdrant.Search(new(Vector), ct)).Matches);
    }

    [Fact]
    public async Task RejectsBadPointsAndLimits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FakeQdrantProvider(), ct);
        var qdrant = brain.Get<IQdrant>(QdrantNames.DefaultNeuron);

        await Assert.ThrowsAsync<QdrantQueryException>(() => qdrant.Upsert(new([]), ct));
        await Assert.ThrowsAsync<QdrantQueryException>(() => qdrant.Upsert(new([new QdrantPoint("greeting", Vector, [])]), ct));
        await Assert.ThrowsAsync<QdrantQueryException>(() => qdrant.Upsert(new([
            new QdrantPoint(PointId, Vector, [new QdrantField("topic", "a"), new QdrantField("topic", "b")]),
        ]), ct));
        await Assert.ThrowsAsync<QdrantQueryException>(() => qdrant.Upsert(new([new QdrantPoint(PointId, [1f, 0f], [])]), ct));
        await Assert.ThrowsAsync<QdrantQueryException>(() => qdrant.Search(new(Vector, 0), ct));
        await Assert.ThrowsAsync<QdrantQueryException>(() => qdrant.Search(new(Vector, QdrantSearch.MaxLimit + 1), ct));
        await Assert.ThrowsAsync<QdrantQueryException>(() => qdrant.Delete(new([]), ct));
    }

    [Fact]
    public async Task ReadConnectionReportsProviderState()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeQdrantProvider();
        await using var brain = await Start(provider, ct);
        var qdrant = brain.Get<IQdrant>(QdrantNames.DefaultNeuron);

        var connection = await qdrant.ReadConnection(ct);

        Assert.True(connection.Connected);
        Assert.Equal(QdrantNames.CollectionName, connection.Collection);
        Assert.Equal("Fake", connection.Provider);
    }

    private static Task<UnitBrain> Start(FakeQdrantProvider provider, CancellationToken ct)
        => UnitTest.Create().WithModule<QdrantModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IQdrantProvider>(provider))
            .StartAsync(ct);
}
