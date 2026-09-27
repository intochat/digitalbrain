using DigitalBrain.Compute.Usage;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class UsageFacts
{
    [Fact]
    public async Task HistorySurvivesReloadAndPagesWithoutDuplicatesOrScopeLeaks()
    {
        var root = Path.Combine(Path.GetTempPath(), "compute-usage-" + Guid.NewGuid());
        var store = new FileUsageStore(root);
        var ct = TestContext.Current.CancellationToken;
        await store.AppendAsync("account", "workspace", "a", "{\"title\":\"original\"}", ct);
        var originalTime = (await store.ReadAsync("account", "workspace", 1, null, ct)).Items[0].OccurredAt;
        await store.AppendAsync("account", "workspace", "b", "{}", ct);
        await store.AppendAsync("account", "workspace", "a", "{\"title\":\"succeeded\"}", ct);
        await store.AppendAsync("account", "workspace", "a", "{\"title\":\"late-cancelled\"}", ct, DateTimeOffset.UnixEpoch);
        await store.AppendAsync("other", "workspace", "secret", "{}", ct);
        await store.AppendAsync("account", "other", "secret", "{}", ct);
        var reloaded = new FileUsageStore(root);
        var first = await reloaded.ReadAsync("account", "workspace", 1, null, ct);
        var second = await reloaded.ReadAsync("account", "workspace", 1, first.NextCursor, ct);
        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Null(second.NextCursor);
        Assert.Equal("b", first.Items[0].Id);
        Assert.Equal(originalTime, second.Items[0].OccurredAt);
        Assert.Contains<UsageRow>([first.Items[0], second.Items[0]], row => row.Payload.Contains("succeeded"));
        await Assert.ThrowsAsync<ArgumentException>(() => reloaded.ReadAsync("other", "workspace", 1, first.NextCursor, ct).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => reloaded.ReadAsync("account", "workspace", 0, null, ct).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => reloaded.ReadAsync("account", "workspace", 20, "invalid", ct).AsTask());
    }
}
