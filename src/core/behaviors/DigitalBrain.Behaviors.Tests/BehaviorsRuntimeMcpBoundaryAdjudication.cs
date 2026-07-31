using Xunit;

namespace DigitalBrain.Behaviors.Tests;

// Plan adjudication (Slice 3 Task 3 / final blockers finding 5):
// Approved plan language does NOT forbid Behaviors.Runtime -> DigitalBrain.Mcp.
//
// docs/superpowers/plans/2026-07-30-digitalbrain-slice-3-tasks-behavior-runtime.md Task 3:
//   Files: HostedBehaviorExecutor.cs AND DigitalBrain.Mcp/McpAuthorizationRail.cs
//   "Implement the control synapse and map existing MCP authorization facts at the module boundary."
//
// docs/superpowers/plans/2026-07-30-digitalbrain-grok-orchestrated-implementation.md Architecture:
//   "provider modules own MCP/auth/account details"
//
// Review/orchestration constraints repeatedly require "MCP remains independent of Tasks" and
// "no MCP -> Tasks dependency" — not forbidding Behaviors composition of MCP types at the
// module boundary. Mapping MCP authorization facts into Task park control is the Behaviors
// runtime's job (BehaviorWorkerNeuron catches McpAuthorizationRequiredException;
// UserActionCompletionBridgeNeuron requires the MCP caller). No architecture RED is warranted.
public sealed class BehaviorsRuntimeMcpBoundaryAdjudication
{
    [Fact(DisplayName =
        "Plan authorizes Behaviors.Runtime composition of DigitalBrain.Mcp at the module boundary — edge is intentional, not a forbidden dependency")]
    public void BehaviorsRuntimeMayReferenceDigitalBrainMcpAtModuleBoundary()
    {
        var runtimeCsproj = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "core",
            "behaviors",
            "DigitalBrain.Behaviors.Runtime",
            "DigitalBrain.Behaviors.Runtime.csproj");
        Assert.True(File.Exists(runtimeCsproj));
        var project = File.ReadAllText(runtimeCsproj);
        Assert.Contains(
            "DigitalBrain.Mcp",
            project,
            StringComparison.Ordinal);

        var worker = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "core",
            "behaviors",
            "DigitalBrain.Behaviors.Runtime",
            "BehaviorWorkerNeuron.cs");
        Assert.True(File.Exists(worker));
        var workerSource = File.ReadAllText(worker);
        Assert.Contains("using DigitalBrain.Mcp;", workerSource, StringComparison.Ordinal);
        Assert.Contains("McpAuthorizationRequiredException", workerSource, StringComparison.Ordinal);

        // MCP must not take a Tasks project dependency (direction the plan actually forbids).
        var mcpCsproj = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "core",
            "mcp",
            "DigitalBrain.Mcp",
            "DigitalBrain.Mcp.csproj");
        Assert.True(File.Exists(mcpCsproj));
        var mcp = File.ReadAllText(mcpCsproj);
        Assert.DoesNotContain(
            "DigitalBrain.Modules.Tasks",
            mcp,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DigitalBrain.Modules.Tasks.Contracts",
            mcp,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }
}
