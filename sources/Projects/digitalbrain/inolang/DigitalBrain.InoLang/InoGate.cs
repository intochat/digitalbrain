using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Testing;

namespace DigitalBrain.InoLang;

// v3 §L6: "no neuron without a green test" is a RUNTIME invariant, not a CI
// convention. The Runtime (Plan 2) calls this before promote/activate.
public sealed record GateDecision(bool CanActivate, string Reason);

public static class InoGate
{
    public static async Task<GateDecision> EvaluateAsync(
        ExecutionPlan plan, CancellationToken ct)
    {
        if (plan.Scenarios.Count == 0)
            return new GateDecision(false,
                $"'{plan.Fqn}' has no scenario — refusing activation (v3 §L6).");

        var report = await new ScenarioRunner().RunAllAsync(plan, ct);
        if (!report.AllPassed)
        {
            var red = report.Results.Where(r => !r.Passed)
                .Select(r => $"\"{r.Name}\": {string.Join("; ", r.Failures)}");
            return new GateDecision(false,
                $"'{plan.Fqn}' has red scenario(s): {string.Join(" | ", red)}");
        }
        return new GateDecision(true, $"'{plan.Fqn}' — all scenarios green.");
    }
}
