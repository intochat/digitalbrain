using DigitalBrain.Qdrant;
using DigitalBrain.Sdk.Vectors;
using Xunit;

namespace DigitalBrain.Modules.Qdrant.Tests.Unit;

public sealed class QdrantFacts
{
    private static readonly Dictionary<string, string> Everything = [];

    [Fact]
    public async Task TheFakeSearchRanksByCosineWithinTheFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var qdrant = new InMemoryQdrant();
        await qdrant.Upsert("things", [
            new("east", [1, 0], new Dictionary<string, string> { ["name"] = "east", ["scope"] = "a" }),
            new("north", [0, 1], new Dictionary<string, string> { ["name"] = "north", ["scope"] = "a" }),
            new("hidden", [1, 0], new Dictionary<string, string> { ["name"] = "hidden", ["scope"] = "b" }),
        ], ct);

        var hits = await qdrant.Search("things", [1, 0.1f], 5, new Dictionary<string, string> { ["scope"] = "a" }, ct);

        Assert.Equal(["east", "north"], hits.Select(hit => hit.Payload["name"]));
    }

    [Fact]
    public async Task TheFakeReadsFromAMissingCollectionReturnNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var qdrant = new InMemoryQdrant();

        Assert.Empty(await qdrant.Search("missing", [1], 5, Everything, ct));
        Assert.Null(await qdrant.ReadPayload("missing", "id", ct));
        Assert.Empty((await qdrant.Scroll("missing", Everything, null, 10, ct)).Points);
        Assert.Equal(0, await qdrant.DeleteWhere("missing", new Dictionary<string, string> { ["scope"] = "a" }, ct));
    }

    [Fact]
    public async Task TheFakeReplacesThePointWhenTheSameKeyIsUpserted()
    {
        var ct = TestContext.Current.CancellationToken;
        var qdrant = new InMemoryQdrant();
        await qdrant.Upsert("things", [new("key", [1], new Dictionary<string, string> { ["version"] = "1" })], ct);
        await qdrant.Upsert("things", [new("key", [1], new Dictionary<string, string> { ["version"] = "2" })], ct);

        Assert.Equal("2", (await qdrant.ReadPayload("things", "key", ct))!["version"]);
        Assert.Single((await qdrant.Scroll("things", Everything, null, 10, ct)).Points);
    }

    [Fact]
    public async Task TheFakeScrollPagesThroughEveryPointOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var qdrant = new InMemoryQdrant();
        await qdrant.Upsert("things", [.. Enumerable.Range(0, 5).Select(index =>
            new VectorPoint("key-" + index, [1], new Dictionary<string, string> { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) }))], ct);

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await qdrant.Scroll("things", Everything, cursor, 2, ct);
            seen.AddRange(page.Points.Select(point => point.Payload["index"]));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(["0", "1", "2", "3", "4"], seen.Order());
    }

    [Fact]
    public async Task TheFakeDeleteWhereRemovesOnlyMatchingPoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var qdrant = new InMemoryQdrant();
        await qdrant.Upsert("things", [
            new("a1", [1], new Dictionary<string, string> { ["scope"] = "a" }),
            new("a2", [1], new Dictionary<string, string> { ["scope"] = "a" }),
            new("b1", [1], new Dictionary<string, string> { ["scope"] = "b" }),
        ], ct);

        Assert.Equal(2, await qdrant.DeleteWhere("things", new Dictionary<string, string> { ["scope"] = "a" }, ct));
        Assert.Equal("b", Assert.Single((await qdrant.Scroll("things", Everything, null, 10, ct)).Points).Payload["scope"]);
    }

    [Theory]
    [InlineData("Endpoint=http://localhost:6334;Key=secret", "http://localhost:6334/", "secret")]
    [InlineData("Endpoint=http://localhost:6334", "http://localhost:6334/", null)]
    public void ConnectionStringsYieldEndpointAndKey(string connectionString, string endpoint, string? key)
    {
        Assert.True(QdrantConnection.TryParse(connectionString, out var parsedEndpoint, out var parsedKey));
        Assert.Equal(endpoint, parsedEndpoint.ToString());
        Assert.Equal(key, parsedKey);
    }

    [Fact]
    public void AConnectionStringWithoutEndpointIsRejected()
        => Assert.False(QdrantConnection.TryParse("Key=secret", out _, out _));
}
