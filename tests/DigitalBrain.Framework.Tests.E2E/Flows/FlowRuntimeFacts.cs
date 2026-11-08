using DigitalBrain;
using DigitalBrain.Flows;
using DigitalBrain.Testing;

namespace DigitalBrain.Framework.Tests.E2E.Flows;

public sealed class FlowRuntimeFixture : BothRuntimesFixture
{
    protected override IReadOnlyList<Type> Modules => [typeof(FlowRuntimeModule)];
}

/// <summary>A flow's steps and wait survive a restart on TestOS and on the silo.</summary>
public sealed class FlowRuntimeFacts(FlowRuntimeFixture fixture) : IClassFixture<FlowRuntimeFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task FlowSurvivesDeactivation(NeuronRuntimeKind kind)
    {
        await using var runtime = await fixture.Start(kind, Ct);
        var id = runtime.Id("test.flow", "survives");
        await runtime.Get<ITestFlow>(id).Start("Build", "ann", null, new FlowBox { AskFirst = true });
        var waiting = await runtime.Get<ITestFlow>(id).Read();
        Assert.Equal(FlowStatus.Waiting, waiting.Status);
        Assert.Equal(["a"], waiting.Done);

        await runtime.Restart(Ct);

        var restored = await runtime.Get<ITestFlow>(id).Read();
        Assert.Equal(FlowStatus.Waiting, restored.Status);
        Assert.Equal("b", restored.WaitingOn);
        await runtime.Get<ITestFlow>(id).Resume("b", WaitOutcome.Arrived, "ann");

        var done = await runtime.Get<ITestFlow>(id).Read();
        Assert.Equal(FlowStatus.Completed, done.Status);
        Assert.Equal(["a", "b"], done.Done);
        var ended = await runtime.WaitForSignals<FlowDone>(id, 1, TimeSpan.FromSeconds(20), Ct);
        Assert.Equal(FlowOutcome.Completed, Assert.Single(ended).Outcome);
    }
}

[Module("flow-runtime-conformance")]
public sealed class FlowRuntimeModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [Need.State("test-flow")], [], []);
}

public sealed class FlowBox
{
    public bool AskFirst { get; init; }

    public int Runs { get; init; }
}

/// <summary>What the facts read back from a flow.</summary>
public sealed record FlowSnapshot(FlowStatus Status, string[] Done, string? WaitingOn, int Runs);

public interface ITestFlow
{
    Task Start(string title, string owner, string? parentFlowId, FlowBox inner);

    Task Resume(string step, WaitOutcome outcome, string? by);

    Task<FlowSnapshot> Read();
}

/// <summary>Two steps; "b" waits for an external signal when <see cref="FlowBox.AskFirst"/>.</summary>
[Neuron("test.flow", State = "test-flow")]
public sealed class TwoStepFlow : Flow<FlowBox>, ITestFlow
{
    protected override IReadOnlyList<string> Steps => ["a", "b"];

    public Task<FlowSnapshot> Read() => Task.FromResult(new FlowSnapshot(State.Status, [.. State.Done], State.Waiting?.Step, State.Inner.Runs));

    protected override Task<StepResult<FlowBox>> Run(string step, FlowBox inner, CancellationToken ct)
    {
        var next = new FlowBox { AskFirst = inner.AskFirst, Runs = inner.Runs + 1 };
        return Task.FromResult(step == "b" && inner.AskFirst
            ? new StepResult<FlowBox>(next, new Waiting("b", WaitReason.External, null, null))
            : new StepResult<FlowBox>(next));
    }
}
