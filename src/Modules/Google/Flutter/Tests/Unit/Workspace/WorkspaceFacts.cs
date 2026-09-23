using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Flutter.Workspace.Signals;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Workspace;

public sealed class WorkspaceFacts
{
    [Fact]
    public async Task SurfaceWindowsCoexistWithLegacyTablesAndKeepReplaySemantics()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        await workspace.Open(new("table-op", "table", "Table", new("legacy-table"), 0));
        var request = new OpenSurfaceWindow("app-op", "app-files", "Files", new("surface", "owner/a/apps/files/surface"), 1);
        await workspace.OpenSurface(request);
        await workspace.Close("app-files", 2);
        await brain.DeactivateAsync(workspace, ct);
        var state = await workspace.OpenSurface(request);
        Assert.Equal(3, state.Revision);
        Assert.False(state.Windows.Single(w => w.Id == "app-files").IsOpen);
        Assert.Equal("legacy-table", state.Windows.Single(w => w.Id == "table").View.Id);
        Assert.Equal("surface", state.Windows.Single(w => w.Id == "app-files").Surface!.Kind);
    }

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
        Assert.False(Assert.Single(replay.State.Windows).IsOpen);
        Assert.Equal(2, replay.State.Revision);
        var reopened = await workspace.Open(request with { OperationId = "explicit-reopen", ExpectedRevision = 2 });
        Assert.True(Assert.Single(reopened.State.Windows).IsOpen);
        Assert.Equal(3, reopened.State.Revision);
    }

    [Fact]
    public async Task OperationRevisionIsStableAfterCloseAndReplay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        var request = new OpenWindow("run/call", "view", "Active leads", new("table"), 0);
        var opened = await workspace.Open(request);
        // The operation log remembers the revision this operation applied, not the latest revision.
        Assert.Equal(1, opened.AppliedRevision);
        await workspace.Close("view", opened.AppliedRevision);
        await brain.DeactivateAsync(workspace, ct);
        var replay = await workspace.Open(request);
        Assert.Equal(opened.AppliedRevision, replay.AppliedRevision);
        Assert.False(Assert.Single(replay.State.Windows).IsOpen);
        Assert.Equal(2, replay.State.Revision);
        // A new operation opens again and reports its own applied revision.
        var reopened = await workspace.Open(request with { OperationId = "run/next", ExpectedRevision = 2 });
        Assert.Equal(3, reopened.AppliedRevision);
        Assert.True(Assert.Single(reopened.State.Windows).IsOpen);
    }

    [Fact]
    public async Task MismatchedOperationIdsFail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        await workspace.Open(new OpenWindow("run/call", "view", "Active leads", new("table"), 0));
        // Reusing the operation id for a different window, title or view is a conflict.
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.Open(new OpenWindow("run/call", "other", "Active leads", new("table"), 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.Open(new OpenWindow("run/call", "view", "Other", new("table"), 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.Open(new OpenWindow("run/call", "view", "Active leads", new("other-table"), 1)));
        Assert.Equal(1, (await workspace.Read()).Revision);
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
        Assert.Equal(result.State.Revision, change.Revision);
        Assert.Equal(change.Revision, (await workspace.Read()).Revision);
    }

    [Fact]
    public async Task RevisionConflictCarriesTheCurrentRevisionForOptimisticRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var workspace = brain.Get<IWorkspace>("owner/a");
        await workspace.Open(new("operation", "window", "Leads", new("table"), 0));
        var conflict = await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => workspace.Close("window", 0));
        Assert.Equal(1, conflict.CurrentRevision);
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

    [Fact]
    public void OperationLogMembersKeepOrleansFieldIdsAndDataShape()
    {
        var storage = typeof(WorkspaceStorage);
        var operationLog = storage.GetProperty("OperationLog");
        var surfaceOperationLog = storage.GetProperty("SurfaceOperationLog");
        var operationRevisionLog = storage.GetProperty("OperationRevisionLog");

        Assert.NotNull(operationLog);
        Assert.NotNull(surfaceOperationLog);
        Assert.NotNull(operationRevisionLog);
        // A rename stays wire-compatible only while each member keeps its Orleans field id.
        Assert.Equal(2, OrleansFieldId(operationLog!));
        Assert.Equal(3, OrleansFieldId(surfaceOperationLog!));
        Assert.Equal(4, OrleansFieldId(operationRevisionLog!));
        // The persisted shape stays a dictionary keyed by operation id.
        Assert.Equal(typeof(Dictionary<string, OpenWindow>), operationLog.PropertyType);
        Assert.Equal(typeof(Dictionary<string, OpenSurfaceWindow>), surfaceOperationLog.PropertyType);
        Assert.Equal(typeof(Dictionary<string, long>), operationRevisionLog.PropertyType);
        // No idempotency member keeps the old receipt name.
        Assert.Null(storage.GetProperty("Receipts"));
        Assert.Null(storage.GetProperty("SurfaceReceipts"));
        Assert.Null(storage.GetProperty("OpenReceiptRevisions"));
    }

    private static int OrleansFieldId(System.Reflection.PropertyInfo property)
    {
        var id = property.GetCustomAttributesData()
            .Single(attribute => attribute.AttributeType.Name == "IdAttribute")
            .ConstructorArguments[0].Value;
        // Orleans boxes the [Id] constructor argument as UInt32, not Int32.
        return Convert.ToInt32(id, System.Globalization.CultureInfo.InvariantCulture);
    }
}