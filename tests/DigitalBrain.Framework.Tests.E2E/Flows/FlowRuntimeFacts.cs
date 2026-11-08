using DigitalBrain;
using DigitalBrain.Confirmation;
using DigitalBrain.Flows;
using DigitalBrain.Runtime.Orleans.Operations;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Testing;

namespace DigitalBrain.Framework.Tests.E2E.Flows;

public sealed class FlowRuntimeFixture : BothRuntimesFixture
{
    protected override IReadOnlyList<Type> Modules => [typeof(FlowRuntimeModule)];
}

/// <summary>A flow's steps and wait survive a restart on TestOS and on the silo.</summary>
/// <summary>Both runtimes with a confirmation gate whose Confirm faults.</summary>
public sealed class FaultingConfirmationsFixture : BothRuntimesFixture
{
    protected override IReadOnlyList<Type> Modules => [typeof(FlowRuntimeModule)];

    protected override void ConfigureSilo(IServiceCollection services)
    {
        services.AddSingleton<IConfirmationContexts, FaultingConfirmations>();
        services.AddSingleton<IConfirmations, FaultingConfirmations>();
    }

    protected override void ConfigureTestOS(IServiceCollection services) => services.AddSingleton<IConfirmations, FaultingConfirmations>();
}

public sealed class FaultingConfirmations : IConfirmations, IConfirmationContexts
{
    public ConfirmationPolicy Policy => ConfirmationPolicy.DestructiveOnly;

    public IConfirmations For(BrainId brain, string requester) => this;

    public async Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
    {
        await Task.Yield();
        throw new InvalidOperationException("inbox down");
    }

    public Task Resolve(string planId, bool approve, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FlowRuntimeFacts(FlowRuntimeFixture fixture, FaultingConfirmationsFixture faulting)
    : IClassFixture<FlowRuntimeFixture>, IClassFixture<FaultingConfirmationsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task FlowSurvivesDeactivation(NeuronRuntimeKind kind)
    {
        await using var runtime = await fixture.Start(kind, Ct);
        var id = runtime.Id("test.flow", "survives");
        await runtime.Get<ITestFlow>(id).Start("Build", "ann", null, new FlowBox { AskFirst = true });
        _ = await runtime.WaitForSignals<FlowWaiting>(id, 1, TimeSpan.FromSeconds(20), Ct);
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

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task StartIsDurableAcrossRestartOnBothRuntimes(NeuronRuntimeKind kind)
    {
        await using var runtime = await fixture.Start(kind, Ct);
        var id = runtime.Id("test.flow", "durable-start");
        await runtime.Get<ITestFlow>(id).Start("Build", "ann", null, new FlowBox());
        Assert.Single(await runtime.Signals<FlowStarted>(id, Ct));

        await runtime.Restart(Ct);

        _ = await runtime.Get<ITestFlow>(id).Read();
        var ended = await runtime.WaitForSignals<FlowDone>(id, 1, TimeSpan.FromSeconds(20), Ct);
        Assert.Equal(FlowOutcome.Completed, Assert.Single(ended).Outcome);
        var done = await runtime.Get<ITestFlow>(id).Read();
        Assert.Equal(["a", "b"], done.Done);
        Assert.Equal(2, done.Runs);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ApprovalWaitResolvesOnBothRuntimes(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var id = runtime.Id("test.flow", "approval");
        await runtime.Get<ITestFlow>(id).Start("Build", "ann", null, new FlowBox { AskFirst = true, Approval = true });
        var waiting = Assert.Single(await runtime.WaitForSignals<FlowWaiting>(id, 1, TimeSpan.FromSeconds(20), Ct));
        Assert.Equal(WaitReason.Approval, waiting.Reason);
        var planId = Assert.IsType<string>(waiting.ConfirmationPlanId);

        await ApproveWhenPending(runtime, planId);

        var ended = await runtime.WaitForSignals<FlowDone>(id, 1, TimeSpan.FromSeconds(20), Ct);
        Assert.Equal(FlowOutcome.Completed, Assert.Single(ended).Outcome);
        var resolved = Assert.Single(await runtime.Signals<FlowResolve>(id, Ct));
        Assert.Equal(WaitOutcome.Approved, resolved.Outcome);
        Assert.Equal(["a", "b"], (await runtime.Get<ITestFlow>(id).Read()).Done);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task FailedConfirmationFailsTheFlowOnBothRuntimes(NeuronRuntimeKind kind)
    {
        var runtime = await faulting.Runtime(kind);
        var id = runtime.Id("test.flow", "faulting");
        await runtime.Get<ITestFlow>(id).Start("Build", "ann", null, new FlowBox { AskFirst = true, Approval = true });

        var ended = await runtime.WaitForSignals<FlowDone>(id, 1, TimeSpan.FromSeconds(20), Ct);

        Assert.Equal(new FlowDone(id.ToString(), FlowOutcome.Failed, "confirmation"), Assert.Single(ended));
        Assert.Contains(new FlowStep(id.ToString(), "b", StepOutcome.Failed, "confirmation: inbox down"), await runtime.Signals<FlowStep>(id, Ct));
        Assert.Equal(FlowStatus.Failed, (await runtime.Get<ITestFlow>(id).Read()).Status);
    }

    // The detached Confirm reaches the confirmations side asynchronously; approve once the plan is pending there.
    private static async Task ApproveWhenPending(INeuronRuntime runtime, string planId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            await runtime.Idle();
            await runtime.ApproveAsPerson(planId, Ct);
            await runtime.Idle();
            if ((await runtime.Signals<FlowResolve>(runtime.Id("test.flow", "approval"), Ct)).Count > 0 || DateTime.UtcNow > deadline) { return; }
            await runtime.Elapse(TimeSpan.FromMilliseconds(100), Ct);
        }
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

    public bool Approval { get; init; }

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

/// <summary>Two steps; "b" waits (for approval when <see cref="FlowBox.Approval"/>, else an external signal) when <see cref="FlowBox.AskFirst"/>.</summary>
[Neuron("test.flow", State = "test-flow")]
public sealed class TwoStepFlow : Flow<FlowBox>, ITestFlow
{
    protected override IReadOnlyList<string> Steps => ["a", "b"];

    public Task<FlowSnapshot> Read() => Task.FromResult(new FlowSnapshot(State.Status, [.. State.Done], State.Waiting?.Step, State.Inner.Runs));

    protected override Task<StepResult<FlowBox>> Run(string step, FlowBox inner, CancellationToken ct)
    {
        var next = new FlowBox { AskFirst = inner.AskFirst, Approval = inner.Approval, Runs = inner.Runs + 1 };
        return Task.FromResult(step == "b" && inner.AskFirst
            ? new StepResult<FlowBox>(next, new Waiting("b", inner.Approval ? WaitReason.Approval : WaitReason.External, null, null))
            : new StepResult<FlowBox>(next));
    }
}
