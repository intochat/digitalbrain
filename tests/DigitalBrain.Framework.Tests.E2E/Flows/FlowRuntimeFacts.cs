using DigitalBrain;
using DigitalBrain.Capabilities.Compute;
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

        // No call reactivates the flow: the journal is polled while time passes, so only the flow's own recovery (the posted
        // advance or the flow-nudge reminder) can complete it.
        var deadline = DateTime.UtcNow.AddMinutes(3);
        IReadOnlyList<FlowDone> ended;
        while ((ended = await runtime.Signals<FlowDone>(id, Ct)).Count == 0 && DateTime.UtcNow < deadline)
        {
            await runtime.Elapse(TimeSpan.FromSeconds(5), Ct);
        }
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

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task AFlowStartedFromAReactionKeepsTheCauseTrace(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var kicker = runtime.Id("test.flow.kicker", "traced");
        var id = runtime.Id("test.flow", "traced");
        await runtime.Get<IFlowStarter>(runtime.Id("test.flow.starter", "traced")).Follow(kicker.ToString());
        var trace = TraceContext.Begin("acct", runtime.Brain.Value, kicker);
        using (TraceContext.Enter(trace)) { await runtime.Get<IFlowKicker>(kicker).Kick("traced"); }

        _ = await runtime.WaitForSignals<FlowStarted>(id, 1, TimeSpan.FromSeconds(20), Ct);

        var journaled = await runtime.Get<ITestFlow>(id).StartedTraceId();
        Assert.Equal(trace.TraceId, journaled);
        Assert.Equal(journaled, await runtime.Get<ITestFlow>(id).TraceId());
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ATopLevelStartHasOneTraceId(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var id = runtime.Id("test.flow", "top-level");
        await runtime.Get<ITestFlow>(id).Start("Build", "ann", null, new FlowBox());

        _ = await runtime.WaitForSignals<FlowStarted>(id, 1, TimeSpan.FromSeconds(20), Ct);

        var journaled = await runtime.Get<ITestFlow>(id).StartedTraceId();
        Assert.False(string.IsNullOrEmpty(journaled));
        Assert.Equal(journaled, await runtime.Get<ITestFlow>(id).TraceId());
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task CancelDuringALongBodyEndsCancelledWithoutConflict(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var id = runtime.Id("test.flow", "cancel-long");
        await runtime.Get<ITestFlow>(id).Start("Build", "ann", null, new FlowBox { Hang = true });
        // The body hangs, so the runtime never idles: poll the journal instead of waiting for idle.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((await runtime.Signals<FlowStep>(id, Ct)).Count == 0 && DateTime.UtcNow < deadline) { await Task.Delay(50, Ct); }

        await runtime.Get<ITestFlow>(id).Cancel("stop");

        var ended = await runtime.WaitForSignals<FlowDone>(id, 1, TimeSpan.FromSeconds(20), Ct);
        Assert.Equal(new FlowDone(id.ToString(), FlowOutcome.Cancelled, "stop"), Assert.Single(ended));
        Assert.DoesNotContain(await runtime.Signals<FlowStep>(id, Ct), step => step.Outcome is StepOutcome.Failed or StepOutcome.Completed);
        var done = await runtime.Get<ITestFlow>(id).Read();
        Assert.Equal(FlowStatus.Cancelled, done.Status);
        Assert.Empty(done.Done);
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
    public static ModuleManifest Manifest { get; } = new([], [], [Need.State("test-flow"), Need.State("test-flow-kick"), Need.Reminders], [], []);
}

public sealed class FlowBox
{
    public bool AskFirst { get; init; }

    public bool Approval { get; init; }

    /// <summary>Step "a" runs until its token is cancelled.</summary>
    public bool Hang { get; init; }

    public int Runs { get; init; }
}

/// <summary>What the facts read back from a flow.</summary>
public sealed record FlowSnapshot(FlowStatus Status, string[] Done, string? WaitingOn, int Runs);

public interface ITestFlow
{
    Task Start(string title, string owner, string? parentFlowId, FlowBox inner);

    Task Resume(string step, WaitOutcome outcome, string? by);

    // A caller outside the silo routes by the contract, so the contract declares the interleave Flow.Cancel needs.
    [Interleaves]
    Task Cancel(string reason);

    Task<FlowSnapshot> Read();

    Task<string> TraceId();

    Task<string?> StartedTraceId();
}

/// <summary>Two steps; "b" waits (for approval when <see cref="FlowBox.Approval"/>, else an external signal) when <see cref="FlowBox.AskFirst"/>.</summary>
[Neuron("test.flow", State = "test-flow")]
public sealed class TwoStepFlow : Flow<FlowBox>, ITestFlow
{
    protected override IReadOnlyList<string> Steps => ["a", "b"];

    public Task<FlowSnapshot> Read() => Task.FromResult(new FlowSnapshot(State.Status, [.. State.Done], State.Waiting?.Step, State.Inner.Runs));

    public Task<string> TraceId() => Task.FromResult(State.TraceId);

    public async Task<string?> StartedTraceId()
        => (await Context.History.Read(Id, 0, 100, CancellationToken.None)).Signals.FirstOrDefault(item => item.Signal is FlowStarted)?.TraceId;

    protected override async Task<StepResult<FlowBox>> Run(string step, FlowBox inner, CancellationToken ct)
    {
        if (step == "a" && inner.Hang) { await Task.Delay(Timeout.Infinite, ct); }
        var next = new FlowBox { AskFirst = inner.AskFirst, Approval = inner.Approval, Hang = inner.Hang, Runs = inner.Runs + 1 };
        return step == "b" && inner.AskFirst
            ? new StepResult<FlowBox>(next, new Waiting("b", inner.Approval ? WaitReason.Approval : WaitReason.External, null, null))
            : new StepResult<FlowBox>(next);
    }
}

[Signal("test.flow.kicked")]
public sealed record FlowKicked(string FlowKey) : Signal;

public sealed record FlowKick
{
    public int Count { get; init; }
}

public interface IFlowKicker
{
    Task Kick(string flowKey);
}

public interface IFlowStarter
{
    Task Follow(string source);
}

[Neuron("test.flow.kicker", State = "test-flow-kick")]
public sealed class FlowKickerNeuron : Neuron<FlowKick>, IFlowKicker
{
    public Task Kick(string flowKey) => Save(State with { Count = State.Count + 1 }, new FlowKicked(flowKey));
}

/// <summary>Starts the flow a kick names from inside its reaction, so the start runs under the kick's cause.</summary>
[Neuron("test.flow.starter")]
public sealed class FlowStarterNeuron : Neuron, IFlowStarter
{
    public Task Follow(string source) => Context.Synapses.Ensure<FlowKicked>(NeuronId.Parse(source));

    [On]
    public Task On(FlowKicked kicked) => Get<ITestFlow>(new NeuronId(Id.Brain, "test.flow", kicked.FlowKey)).Start("Build", "ann", null, new FlowBox());
}
