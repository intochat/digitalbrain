using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Flutter.Workspace.Signals;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Workspace;

public sealed class WorkspaceFacts
{
    [Fact]
    public async Task ReplayAfterCloseNeverReopensAWindowAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        var request = new OpenWindow("run/call", "view", "Active leads", new("table"), 0);
        await workspace.Open(request);
        await workspace.Open(request);
        Assert.Single((await workspace.Read()).Windows);
        await workspace.Close("view", 1);
        await brain.DeactivateAsync(workspace, ct);
        var replay = await workspace.Open(request);
        Assert.False(Assert.Single(replay.Windows).IsOpen);
        Assert.Equal(2, replay.Revision);
        var reopened = await workspace.Open(request with { OperationId = "explicit-reopen", ExpectedRevision = 2 });
        Assert.True(Assert.Single(reopened.Windows).IsOpen);
        Assert.Equal(3, reopened.Revision);
    }

    [Fact]
    public async Task StaleCommandsAndConflictingOperationReuseLeaveStateUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        var request = new OpenWindow("run/call", "view", "Active leads", new("table"), 0);
        await workspace.Open(request);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => workspace.Close("view", 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.Open(request with { View = new("another-table") }));
        Assert.Equal(1, (await workspace.Read()).Revision);
        Assert.Empty((await brain.Get<IWorkspace>("owner/b").Read()).Windows);
    }

    [Fact]
    public async Task SuccessfulMutationPublishesItsPersistedRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        await using var changes = await brain.Observe<WorkspaceChanged>(workspace, ct);
        var result = await workspace.Open(new("operation", "window", "Leads", new("table"), 0));
        var change = await changes.NextAsync(ct: ct);
        Assert.Equal("owner/a", change.WorkspaceId);
        Assert.Equal(result.Revision, change.Revision);
        Assert.Equal(change.Revision, (await workspace.Read()).Revision);
    }

    [Theory]
    [InlineData("", "window", "Title")]
    [InlineData("operation", "", "Title")]
    [InlineData("operation", "window", " ")]
    public async Task InvalidIdentifiersDoNotCreateState(string operation, string window, string title)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        await Assert.ThrowsAsync<ArgumentException>(() => workspace.Open(new(operation, window, title, new("table"), 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => workspace.Open(new(new string('x', 257), "window", "Title", new("table"), 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => workspace.Open(new("operation", "window", new string('x', 201), new("table"), 0)));
        Assert.Empty((await workspace.Read()).Windows);
    }
}
