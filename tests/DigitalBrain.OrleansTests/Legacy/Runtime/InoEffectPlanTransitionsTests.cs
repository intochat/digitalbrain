using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoEffectPlanTransitionsTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2026, 7, 14, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Put_binds_an_immutable_plan_to_its_grain_key()
    {
        var empty = InoEffectPlanState.Empty();
        var plan = Plan();

        var stored = InoEffectPlanTransitions.Put(empty, PlanId, plan);

        Assert.Equal(1, stored.Revision);
        Assert.NotNull(stored.Plan);
        Assert.Equal(plan.PlanId, stored.Plan.PlanId);
        Assert.Equal(plan.ActorScope, stored.Plan.ActorScope);
        Assert.Equal(plan.OperationId, stored.Plan.OperationId);
        Assert.Equal(plan.ToolId, stored.Plan.ToolId);
        Assert.Equal(plan.PayloadUtf8, stored.Plan.PayloadUtf8);
        Assert.Equal(plan.SafeSummary, stored.Plan.SafeSummary);
        Assert.Equal(plan.ExpiresAt, stored.Plan.ExpiresAt);
        Assert.Null(stored.Completion);
        InoEffectPlanTransitions.ValidateState(stored);
        Assert.Same(stored, InoEffectPlanTransitions.Put(stored, PlanId, plan));
        Assert.Throws<RuntimeStateIntegrityException>(() =>
            InoEffectPlanTransitions.Put(stored, PlanId, plan with { ToolId = "salesforce.record.update" }));
    }

    [Fact]
    public void Resolve_scrubs_the_provider_payload_and_is_idempotent()
    {
        var stored = InoEffectPlanTransitions.Put(InoEffectPlanState.Empty(), PlanId, Plan());
        var decision = Decision(InoEffectTerminalKind.Approved);
        var completion = new InoEffectPlanCompletion(
            InoToolEffectDisposition.Succeeded,
            "The approved email was sent.");

        var completed = InoEffectPlanTransitions.Resolve(stored, decision, completion);

        Assert.Equal(2, completed.Revision);
        Assert.Empty(completed.Plan!.PayloadUtf8);
        Assert.Equal(decision, completed.Decision);
        Assert.Equal(completion, completed.Completion);
        InoEffectPlanTransitions.ValidateState(completed);
        Assert.Same(completed, InoEffectPlanTransitions.Resolve(completed, decision, completion));
        Assert.Throws<RuntimeStateIntegrityException>(() => InoEffectPlanTransitions.Resolve(
            completed,
            decision with { DecisionId = "decision-2", TerminalKind = InoEffectTerminalKind.Failed },
            completion with { Disposition = InoToolEffectDisposition.Failed }));
    }

    [Theory]
    [InlineData(InoEffectTerminalKind.Expired)]
    [InlineData(InoEffectTerminalKind.Failed)]
    [InlineData(InoEffectTerminalKind.OutcomeUnknown)]
    public void Every_terminal_path_scrubs_the_plan_payload(InoEffectTerminalKind kind)
    {
        var stored = InoEffectPlanTransitions.Put(InoEffectPlanState.Empty(), PlanId, Plan());
        var disposition = kind == InoEffectTerminalKind.OutcomeUnknown
            ? InoToolEffectDisposition.OutcomeUnknown
            : InoToolEffectDisposition.Failed;

        var terminal = InoEffectPlanTransitions.Resolve(
            stored,
            Decision(kind),
            new InoEffectPlanCompletion(disposition, "No external action remains pending."));

        Assert.Empty(terminal.Plan!.PayloadUtf8);
        Assert.Equal(kind, terminal.Decision!.TerminalKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public void Provider_payload_must_be_present_and_bounded(int length)
    {
        var payload = new byte[length];

        Assert.Throws<ArgumentException>(() =>
            InoEffectPlanTransitions.Put(InoEffectPlanState.Empty(), PlanId, Plan() with { PayloadUtf8 = payload }));
    }

    private static InoEffectPlan Plan() => new(
        PlanId,
        ActorScope,
        "operation-1",
        "gmail.send",
        "{\"safe\":true}"u8.ToArray(),
        "send an email to the approved test recipient",
        ExpiresAt);

    private static InoEffectDecision Decision(InoEffectTerminalKind kind) => new(
        "decision-1",
        ActorScope,
        kind,
        ExpiresAt.AddMinutes(-1));

    private const string PlanId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ActorScope = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
}
