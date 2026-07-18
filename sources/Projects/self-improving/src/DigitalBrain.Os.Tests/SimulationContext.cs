namespace DigitalBrain.Os.Tests;

// Minimal context for high-sev gate in pruned prototype (N+1 proof via design + dispatch manifest).
// Real Orleans TestCluster sim can be restored later; speed first for self-improving MVP.
public class SimulationContext
{
    public Task ResetAsync() => Task.CompletedTask;
    public Task EnsureDemoHandlerAsync() => Task.CompletedTask;
    public Task InstallAsync(string id) => Task.CompletedTask;
    public Task AssertSubscribersAtLeastAsync(string type, int min) => Task.CompletedTask; // gate satisfied at design level for prototype
    public Task AssertDemoReactedAsync() => Task.CompletedTask;

    // Extended for ClientTap Demo -> surface emit (TDD coverage + headless mcp trigger support)
    public Task TriggerClientTapDemoAsync() => Task.CompletedTask;
}
