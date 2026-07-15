using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
namespace DigitalBrain.Kernel.Runtime;

[GenerateSerializer, Alias("digitalbrain.runtime.ino-effect-plan")]
public sealed record InoEffectPlan(
    [property: Id(0)] string PlanId,
    [property: Id(1)] string ActorScope,
    [property: Id(2)] string OperationId,
    [property: Id(3)] string ToolId,
    [property: Id(4)] byte[] PayloadUtf8,
    [property: Id(5)] string SafeSummary,
    [property: Id(6)] DateTimeOffset ExpiresAt);
[GenerateSerializer, Alias("digitalbrain.runtime.ino-effect-plan-completion")]
public sealed record InoEffectPlanCompletion([property: Id(0)] InoToolEffectDisposition Disposition, [property: Id(1)] string SafeResult);
[GenerateSerializer, Alias("digitalbrain.runtime.ino-effect-terminal-kind")]
public enum InoEffectTerminalKind
{
    None = 0,
    Approved = 1,
    Declined = 2,
    Expired = 3,
    Failed = 4,
    OutcomeUnknown = 5
}
[GenerateSerializer, Alias("digitalbrain.runtime.ino-effect-decision")]
public sealed record InoEffectDecision(
    [property: Id(0)] string DecisionId,
    [property: Id(1)] string ActorScope,
    [property: Id(2)] InoEffectTerminalKind TerminalKind,
    [property: Id(3)] DateTimeOffset ResolvedAt);
[GenerateSerializer, Alias("digitalbrain.runtime.ino-effect-plan-state")]
public sealed record InoEffectPlanState(
    [property: Id(0)] int SchemaVersion,
    [property: Id(1)] long Revision,
    [property: Id(2)] InoEffectPlan? Plan,
    [property: Id(3)] InoEffectPlanCompletion? Completion,
    [property: Id(4)] InoEffectDecision? Decision = null)
{
    public static InoEffectPlanState Empty() => new(RuntimeStateSchemas.InoEffectPlan, 0, null, null);
}
[Alias("digitalbrain.runtime.i-ino-effect-plan-neuron")]
public interface IInoEffectPlanNeuron : IGrainWithStringKey
{
    [Alias("digitalbrain.runtime.ino-effect-plan.put")]
    Task PutAsync(InoEffectPlan plan);
    [Alias("digitalbrain.runtime.ino-effect-plan.decline")]
    Task<InoToolEffectResult> DeclineAsync(
        string actorScope,
        string decisionId,
        CancellationToken cancellationToken = default);
    [Alias("digitalbrain.runtime.ino-effect-plan.read-decision")]
    Task<InoEffectDecision?> ReadDecisionAsync(
        string actorScope,
        CancellationToken cancellationToken = default);
    [Alias("digitalbrain.runtime.ino-effect-plan.execute")]
    Task<InoToolEffectResult> ExecuteAsync(
        string actorScope,
        string operationId,
        string toolId,
        string summaryDigest,
        string effectId,
        string providerIdempotencyKey,
        string executionProof,
        CancellationToken cancellationToken = default);
}
public static class InoEffectPlanTransitions
{
    public const int MaximumPayloadBytes = 64 * 1024;
    public const int MaximumSafeTextLength = 512;
    public static InoEffectPlanState Put(InoEffectPlanState state, string planId, InoEffectPlan plan)
    {
        ValidateState(state);
        ValidatePlan(plan, requirePayload: true);
        RuntimeStateKeys.DemandScopeHash(planId);
        if (!string.Equals(plan.PlanId, planId, StringComparison.Ordinal))
            throw new RuntimeStateIntegrityException("effect plan grain key is invalid");
        if (state.Plan is null)
            return state with { Revision = 1, Plan = Clone(plan) };
        if (state.Completion is null && SamePlan(state.Plan, plan)) return state;
        throw new RuntimeStateIntegrityException("immutable effect plan changed");
    }
    public static InoEffectPlanState Resolve(
        InoEffectPlanState state,
        InoEffectDecision decision,
        InoEffectPlanCompletion completion)
    {
        ValidateState(state);
        ValidateDecision(decision, completion);
        ValidateCompletion(completion);
        if (state.Plan is null)
            throw new InvalidOperationException("An effect plan must be stored before it can complete.");
        if (state.Completion is not null)
        {
            if (state.Decision == decision && state.Completion == completion) return state;
            throw new RuntimeStateIntegrityException("immutable effect plan decision changed");
        }
        var completed = state with
        {
            Revision = checked(state.Revision + 1),
            Plan = state.Plan with { PayloadUtf8 = [] },
            Completion = completion,
            Decision = decision
        };
        ValidateState(completed);
        return completed;
    }
    public static void ValidateState(InoEffectPlanState state)
    {
        if (state.SchemaVersion != RuntimeStateSchemas.InoEffectPlan || state.Revision is < 0 or > 2 ||
            state.Revision == 0 && (state.Plan is not null || state.Completion is not null || state.Decision is not null) ||
            state.Revision == 1 && (state.Plan is null || state.Completion is not null || state.Decision is not null) ||
            state.Revision == 2 && (state.Plan is null || state.Completion is null))
            throw new RuntimeStateIntegrityException("invalid effect plan state");
        if (state.Plan is not null)
            ValidatePlan(state.Plan, requirePayload: state.Completion is null);
        if (state.Completion is not null)
        {
            if (state.Plan!.PayloadUtf8.Length != 0)
                throw new RuntimeStateIntegrityException("completed effect plan retained provider payload");
            ValidateCompletion(state.Completion);
            if (state.Decision is not null)
                ValidateDecision(state.Decision, state.Completion);
        }
    }
    public static void ValidatePlan(InoEffectPlan plan, bool requirePayload)
    {
        if (!RuntimeStateKeys.IsScopeHash(plan.PlanId) || !RuntimeStateKeys.IsScopeHash(plan.ActorScope) || !IsBounded(plan.OperationId, 128) || !IsToolId(plan.ToolId) ||
            plan.PayloadUtf8 is null || plan.PayloadUtf8.Length > MaximumPayloadBytes ||
            requirePayload && plan.PayloadUtf8.Length == 0 || !requirePayload && plan.PayloadUtf8.Length != 0 ||
            !IsBounded(plan.SafeSummary, MaximumSafeTextLength) || plan.ExpiresAt == default)
            throw new ArgumentException("Effect plan metadata is invalid.", nameof(plan));
    }
    private static void ValidateCompletion(InoEffectPlanCompletion completion)
    {
        if (!Enum.IsDefined(completion.Disposition) || !IsBounded(completion.SafeResult, MaximumSafeTextLength))
            throw new ArgumentException("Effect plan completion is invalid.", nameof(completion));
    }
    private static void ValidateDecision(InoEffectDecision decision, InoEffectPlanCompletion completion)
    {
        var matchingDisposition = decision.TerminalKind switch
        {
            InoEffectTerminalKind.Approved => completion.Disposition == InoToolEffectDisposition.Succeeded,
            InoEffectTerminalKind.Declined or InoEffectTerminalKind.Expired or InoEffectTerminalKind.Failed =>
                completion.Disposition == InoToolEffectDisposition.Failed,
            InoEffectTerminalKind.OutcomeUnknown => completion.Disposition == InoToolEffectDisposition.OutcomeUnknown,
            _ => false
        };
        if (!IsBounded(decision.DecisionId, 256) || !RuntimeStateKeys.IsScopeHash(decision.ActorScope) ||
            decision.ResolvedAt == default || decision.ResolvedAt.Offset != TimeSpan.Zero || !matchingDisposition)
            throw new ArgumentException("Effect plan decision is invalid.", nameof(decision));
    }
    private static bool SamePlan(InoEffectPlan first, InoEffectPlan second) =>
        string.Equals(first.PlanId, second.PlanId, StringComparison.Ordinal) &&
        string.Equals(first.ActorScope, second.ActorScope, StringComparison.Ordinal) &&
        string.Equals(first.OperationId, second.OperationId, StringComparison.Ordinal) &&
        string.Equals(first.ToolId, second.ToolId, StringComparison.Ordinal) &&
        first.PayloadUtf8.SequenceEqual(second.PayloadUtf8) &&
        string.Equals(first.SafeSummary, second.SafeSummary, StringComparison.Ordinal) &&
        first.ExpiresAt == second.ExpiresAt;
    private static InoEffectPlan Clone(InoEffectPlan plan) => plan with { PayloadUtf8 = plan.PayloadUtf8.ToArray() };
    private static bool IsToolId(string value) =>
        IsBounded(value, 128) && value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
    private static bool IsBounded(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);
}
public static class ExternalEffectGrants
{
    public static void Demand(string? effectKind, IReadOnlySet<string> grants)
    {
        if (string.IsNullOrWhiteSpace(effectKind) || !grants.Contains(effectKind))
            throw new UnauthorizedAccessException("The authenticated session cannot approve this external action.");
    }
}
