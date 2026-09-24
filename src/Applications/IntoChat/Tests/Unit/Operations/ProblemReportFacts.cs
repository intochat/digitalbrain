using IntoChat.Operations;

namespace IntoChat.Tests.Operations;

public sealed class ProblemReportFacts
{
    [Fact]
    public async Task AReportKeepsTheIntentIdItCameFrom()
    {
        var store = new ProblemReportStore();
        var report = await store.AddAsync("workspace-1", "run-42", "The table did not open.", TestContext.Current.CancellationToken);

        Assert.Equal("run-42", report.IntentId);
        Assert.Equal("workspace-1", report.WorkspaceId);
        Assert.Single(await store.ListAsync("workspace-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEmptyIntentIdIsRejected()
    {
        var store = new ProblemReportStore();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddAsync("workspace-1", "  ", "something broke", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReportsAreScopedToTheirWorkspace()
    {
        var store = new ProblemReportStore();
        await store.AddAsync("workspace-1", "run-1", "first", TestContext.Current.CancellationToken);
        await store.AddAsync("workspace-2", "run-2", "second", TestContext.Current.CancellationToken);

        Assert.Single(await store.ListAsync("workspace-1", TestContext.Current.CancellationToken));
        Assert.Equal("run-2", (await store.ListAsync("workspace-2", TestContext.Current.CancellationToken)).Single().IntentId);
    }
}
