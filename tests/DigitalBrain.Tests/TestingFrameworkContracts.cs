using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TestingFrameworkContracts
{
    [Fact(DisplayName = "Simulations.OpenAsync returns a Scenario with a non-default unique Owner")]
    public async Task OpenAsyncReturnsScenarioWithNonDefaultOwner()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(default, scenario.Owner);
        Assert.False(string.IsNullOrWhiteSpace(scenario.Owner.Value));
    }

    [Fact(DisplayName = "Simulations.OpenAsync(OwnerId) preserves the specified Owner")]
    public async Task OpenAsyncWithOwnerPreservesOwner()
    {
        var owner = new OwnerId("gherkin-specified-owner");
        await using var scenario = await Simulations.OpenAsync(owner, TestContext.Current.CancellationToken);

        Assert.Equal(owner, scenario.Owner);
    }

    [Fact(DisplayName = "Simulations.OpenAsync(string) preserves the specified Owner value")]
    public async Task OpenAsyncWithStringOwnerPreservesOwner()
    {
        await using var scenario = await Simulations.OpenAsync(
            "gherkin-string-owner",
            TestContext.Current.CancellationToken);

        Assert.Equal(new OwnerId("gherkin-string-owner"), scenario.Owner);
    }

    [Fact(DisplayName = "sequential Simulations.OpenAsync calls receive different Owners")]
    public async Task SequentialOpensReceiveDifferentOwners()
    {
        await using var first = await Simulations.OpenAsync(TestContext.Current.CancellationToken);
        await using var second = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Owner, second.Owner);
    }

    [Fact(DisplayName = "disposing a Scenario does not stop the assembly cluster for the next OpenAsync")]
    public async Task DisposeDoesNotStopAssemblyCluster()
    {
        var first = await Simulations.OpenAsync(TestContext.Current.CancellationToken);
        await first.DisposeAsync();

        await using var second = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(second.Grains);
        Assert.NotEqual(default, second.Owner);
    }

    [Fact(DisplayName = "Scenario.Grains is a live grain factory after OpenAsync without calling SimulationCluster.StartAsync")]
    public async Task OpenAsyncStartsClusterAndExposesLiveGrains()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(scenario.Grains);
        Assert.NotNull(scenario.Clock);
        Assert.IsType<ScenarioClock>(scenario.Clock);
        Assert.NotSame(TimeProvider.System, scenario.Clock);
    }

    [Fact(DisplayName = "Scenario.Clock tracks wall time so reminders and real liveness keep progressing")]
    public async Task ClockTracksWallTimeBetweenReads()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        var first = scenario.Clock.GetUtcNow();
        await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        var second = scenario.Clock.GetUtcNow();

        Assert.True(second >= first);
        Assert.NotEqual(default, first);
    }

    [Fact(DisplayName = "Scenario.AdvanceClock adds a jump on top of wall time without sleeping the jump")]
    public async Task AdvanceClockMovesGetUtcNow()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        var before = scenario.Clock.GetUtcNow();
        scenario.AdvanceClock(TimeSpan.FromMinutes(5));
        var after = scenario.Clock.GetUtcNow();

        Assert.True(after >= before + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.True(after < before + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(2));
    }

    [Fact(DisplayName = "host ScenarioClock is registered in silo DI so neuron journal stamps follow AdvanceClock")]
    public async Task AdvanceClockStampsNeuronJournalDeliveries()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        scenario.AdvanceClock(TimeSpan.FromHours(3));
        var expected = scenario.Clock.GetUtcNow();

        var sessionId = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "session");
        var peerId = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "peer");
        var session = scenario.Grains.GetGrain<ISessionNeuron>(sessionId.ToGrainId());

        await session.FireAsync(peerId, new CapabilityRequested("IProbe", "Ping", peerId));

        var journal = await session.ReadNeuronJournalAsync(sessionId, JournalKind.Outgoing, afterSequence: 0);
        var stamped = Assert.Single(journal.Delta);

        Assert.True(stamped.Timestamp >= expected - TimeSpan.FromSeconds(2));
        Assert.True(stamped.Timestamp <= scenario.Clock.GetUtcNow() + TimeSpan.FromSeconds(2));
        Assert.True(stamped.Timestamp >= DateTimeOffset.UtcNow + TimeSpan.FromHours(2));
    }

    [Fact(DisplayName = "Scenario.Arm journal commit fault fails the next write without FailJournalWriteAfter")]
    public async Task ArmJournalCommitAfterFailsNextWriteWithoutStaticFailApi()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        var sessionId = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "session");
        var peerId = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "peer");
        var session = scenario.Grains.GetGrain<ISessionNeuron>(sessionId.ToGrainId());

        await using var fault = scenario.Arm(new JournalCommitAfter(
            sessionId.ToGrainId(),
            CompletedWritesBeforeFailure: 0,
            Message: "injected scenario journal commit failure"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.FireAsync(peerId, new CapabilityRequested("IProbe", "Ping", peerId)));

        Assert.Equal("injected scenario journal commit failure", error.Message);
    }

    [Fact(DisplayName = "disposing Scenario while a fault is still armed fails the test")]
    public async Task DisposeWithArmedFaultFails()
    {
        var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);
        var grain = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "armed").ToGrainId();

        _ = scenario.Arm(new JournalCommitAfter(
            grain,
            CompletedWritesBeforeFailure: 0,
            Message: "left armed on purpose"));

        var error = await Assert.ThrowsAsync<SimulationAssertionException>(async () =>
            await scenario.DisposeAsync());

        Assert.Contains("still armed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Dispose after Open/AdvanceClock/Arm with leftover fault attaches artifact with Owner and stages")]
    public async Task DisposeArmedFaultAttachesArtifactWithOwnerAndStages()
    {
        var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);
        var owner = scenario.Owner;
        scenario.AdvanceClock(TimeSpan.FromMinutes(2));

        var grain = new NeuronId(ISessionNeuron.GrainTypeName, owner, "artifact").ToGrainId();
        _ = scenario.Arm(new JournalCommitAfter(
            grain,
            CompletedWritesBeforeFailure: 0,
            Message: "artifact armed fault"));

        var error = await Assert.ThrowsAsync<SimulationAssertionException>(async () =>
            await scenario.DisposeAsync());

        Assert.NotNull(error.Artifact);
        Assert.Equal(owner, error.Artifact.Owner);
        Assert.Contains(ScenarioStages.Open, error.Artifact.Stages);
        Assert.Contains(ScenarioStages.AdvanceClock, error.Artifact.Stages);
        Assert.Contains(ScenarioStages.Arm, error.Artifact.Stages);
        Assert.Contains(ScenarioStages.Dispose, error.Artifact.Stages);
        Assert.True(error.Artifact.ArmedFaultCount >= 1);
        Assert.NotEmpty(error.Artifact.ArmedFaultDescriptions);
        Assert.Contains(owner.Value, error.Message, StringComparison.Ordinal);
        Assert.Contains(ScenarioStages.AdvanceClock, error.Message, StringComparison.Ordinal);
        Assert.Contains(ScenarioStages.Arm, error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "disposing FaultHandle then Scenario succeeds without throwing or requiring an artifact")]
    public async Task DisposeAfterDisarmSucceeds()
    {
        var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);
        var grain = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "disarmed").ToGrainId();

        var fault = scenario.Arm(new JournalCommitAfter(
            grain,
            CompletedWritesBeforeFailure: 0,
            Message: "disarmed before scenario dispose"));

        await fault.DisposeAsync();
        await scenario.DisposeAsync();
    }

    [Fact(DisplayName = "TestingEdges.Closed is the locked external substitute list")]
    public void TestingEdgesClosedListIsLocked()
    {
        Assert.Equal(
            [
                TestingEdges.ChatClient,
                TestingEdges.SouthboundMcpTransport,
                TestingEdges.OAuthAndParams,
                TestingEdges.TimeProvider,
            ],
            TestingEdges.Closed);

        Assert.Equal("IChatClient", TestingEdges.ChatClient);
        Assert.Equal("southbound MCP transport", TestingEdges.SouthboundMcpTransport);
        Assert.Equal("OAuth/params", TestingEdges.OAuthAndParams);
        Assert.Equal("TimeProvider", TestingEdges.TimeProvider);
    }
}
