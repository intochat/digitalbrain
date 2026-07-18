using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang.Planning;

namespace DigitalBrain.Kernel.Runtime;

// Silo-local plan cache. Returns a Result-shaped PlanCacheEntry so refusals
// stay exception-free across grain boundaries (CLAUDE.md: "emit a failure
// synapse, don't throw across the cortex"; v3 §L6 enforced as a gate).
public interface IPlanCache
{
    ValueTask<PlanCacheEntry> GetOrCompileAsync(NeuronDescriptor descriptor, CancellationToken ct);
}

public sealed record PlanCacheEntry(ExecutionPlan? Plan, ScenarioGateResult Gate)
{
    public static PlanCacheEntry Activated(ExecutionPlan plan, string fqn) =>
        new(plan, ScenarioGateResult.Activated(fqn));

    public static PlanCacheEntry Refused(string fqn, string reason) =>
        new(null, ScenarioGateResult.Refused(fqn, reason));
}
