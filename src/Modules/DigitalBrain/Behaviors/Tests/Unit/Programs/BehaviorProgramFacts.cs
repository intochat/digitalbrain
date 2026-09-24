using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorProgramFacts
{
    [Fact]
    public async Task CommandsAreDurableAndConflictingRetriesFail()
    {
        var documents = new InMemoryDocumentStore<BehaviorProgramDocument>();
        var store = new BehaviorProgramStore(documents);
        var request = new DeployBehavior(0, Guid.NewGuid(), new("id", "source", "environment"), "{}");
        var deployed = await store.DeployAsync("program", request, TestContext.Current.CancellationToken);
        var reopened = new BehaviorProgramStore(documents);
        var replay = await reopened.DeployAsync("program", request, TestContext.Current.CancellationToken);
        Assert.Equal(deployed.Revision, replay.Revision);
        Assert.Equal(deployed.Deployments.ToArray(), replay.Deployments.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.DeployAsync("program", request with { ConfigurationJson = "{\"Behavior__value\":\"1\"}" }, TestContext.Current.CancellationToken));
        var stopped = await reopened.ChangeAsync("program", new(deployed.Revision, Guid.NewGuid()), false, TestContext.Current.CancellationToken);
        Assert.Equal(BehaviorDesiredState.Stopped, stopped.DesiredState);
        Assert.Equal(BehaviorDesiredState.Stopped, (await new BehaviorProgramStore(documents).ReadAsync("program", TestContext.Current.CancellationToken)).DesiredState);
    }

    [Fact]
    public async Task DeletedProgramStopsAndLeavesTheLiveList()
    {
        var documents = new InMemoryDocumentStore<BehaviorProgramDocument>();
        var store = new BehaviorProgramStore(documents);
        var deployed = await store.DeployAsync("program", new DeployBehavior(0, Guid.NewGuid(), new("id", "source", "environment"), "{}"), TestContext.Current.CancellationToken);
        var operation = Guid.NewGuid();
        var deleted = await store.RemoveAsync("program", new(deployed.Revision, operation), TestContext.Current.CancellationToken);
        Assert.Equal(BehaviorDesiredState.Stopped, deleted.DesiredState);
        Assert.Equal(BehaviorExecutionState.Stopping, deleted.State);
        Assert.Empty(await store.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(deleted.Revision, (await store.ReadAsync("program", TestContext.Current.CancellationToken)).Revision);
        var replay = await store.ReplayAsync("program", "delete", operation, new DeleteBehavior(deployed.Revision, operation), TestContext.Current.CancellationToken);
        Assert.Equal(deleted.Revision, replay!.Revision);
    }
}
