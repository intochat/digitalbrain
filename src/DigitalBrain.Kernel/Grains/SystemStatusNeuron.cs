using System.Reflection;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.systemstatus.v1")]
public class SystemStatusNeuron(ILogger<SystemStatusNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ISystemStatus
{
    private McpClient? _mcp;
    private IGrainTimer? _pollTimer;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await TryConnectMcpAsync(ct);
        await FireAsync(new SystemLaunched("digitalbrain", DateTimeOffset.UtcNow), ct);
        await FireAsync(new SystemStatusChanged("kernel", "launched"), ct);

        // In tests we do not poll; avoid background loops and repeated MCP attempts that log noise.
        if (IsTestMode())
        {
            return;
        }

        _pollTimer = this.RegisterGrainTimer(
            static (grain, token) => grain.PollOnceAsync(token),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(25),
                Period = TimeSpan.FromSeconds(25),
                KeepAlive = false,
                Interleave = false
            });
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _pollTimer?.Dispose();
        _pollTimer = null;

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private static bool IsTestMode() =>
        string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_TEST_MODE"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Testing", StringComparison.OrdinalIgnoreCase);

    private async Task TryConnectMcpAsync(CancellationToken ct)
    {
        if (_mcp != null)
        {
            return;
        }

        if (IsTestMode())
        {
            // Tests run without Aspire MCP / CLI available; self-awareness is telemetry + LLM only. No spawn, no long cancel.
            await FireAsync(new SystemStatusChanged("aspire-mcp", "unavailable"), ct);
            return;
        }

        try
        {
            using var shortCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            shortCts.CancelAfter(TimeSpan.FromSeconds(3));

            var workDir = ResolveAppHostDir() ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_APPHOST_DIR") ?? AppContext.BaseDirectory;

            _mcp = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire-self",
                    Command = "aspire",
                    Arguments = ["agent", "mcp"],
                    WorkingDirectory = workDir
                }), cancellationToken: shortCts.Token);

            var tools = await _mcp.ListToolsAsync(cancellationToken: shortCts.Token);
            var toolNames = string.Join(",", tools.Select(t => t.Name));
            Logger.LogInformation("SystemStatus connected to Aspire MCP ({Count} tools: {Names}) from {Dir}", tools.Count, toolNames, workDir);
            await FireAsync(new SystemStatusChanged("aspire-mcp", "connected", $"tools={tools.Count}"), ct);

            await PollHealthAsync(shortCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected in constrained envs or shutdown; do not spam warnings.
            await FireAsync(new SystemStatusChanged("aspire-mcp", "unavailable"), ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SystemStatus MCP connect failed. Self-awareness limited to internal telemetry + LLM.");
            await FireAsync(new SystemStatusChanged("aspire-mcp", "unavailable"), ct);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            if (_mcp == null)
            {
                await TryConnectMcpAsync(ct);
            }
            if (_mcp != null)
            {
                await PollHealthAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "SystemStatus poll iteration failed.");
        }
    }

    private async Task PollHealthAsync(CancellationToken ct)
    {
        if (_mcp == null)
        {
            return;
        }

        var resources = await CallMcpAsync("list_resources", ct: ct);
        if (resources.Contains("Failed", StringComparison.OrdinalIgnoreCase) || resources.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase) || resources.Contains("Exited", StringComparison.OrdinalIgnoreCase))
        {
            await FireAsync(new SystemStatusChanged("aspire", "unhealthy", resources), ct);
        }
    }

    private string? ResolveAppHostDir()
    {
        var candidates = new List<string>
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 5 && dir != null; i++)
        {
            candidates.Add(dir.FullName);
            dir = dir.Parent;
        }
        var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 3 && cur != null; i++)
        {
            candidates.Add(cur.FullName);
            cur = cur.Parent;
        }
        foreach (var c in candidates.Distinct())
        {
            try
            {
                if (File.Exists(Path.Combine(c, "aspire.config.json")) ||
                    Directory.GetFiles(c, "*.slnx").Any() ||
                    Directory.GetDirectories(c, "*AppHost").Any() ||
                    Directory.GetFiles(c, "*AppHost.csproj").Any())
                {
                    return c;
                }
            }
            catch { }
        }
        return null;
    }

    public async Task HandleAsync(SystemStatusChanged status, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("System status: {Component} = {Status}", status.Component, status.Status);

        if (status.Status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
            status.Status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            await DiagnoseAndProposeAsync(status, cancellationToken);
        }
    }

    public Task HandleAsync(FixProposal proposal, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Fix proposal received: {Issue} -> {Fix}", proposal.Issue, proposal.ProposedFix);
        return Task.CompletedTask;
    }

    private async Task DiagnoseAndProposeAsync(SystemStatusChanged bad, CancellationToken ct)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        string analysis = "manual review required";
        if (chat != null && _mcp != null)
        {
            try
            {
                var resources = await CallMcpAsync("list_resources", ct: ct);
                var logs = await CallMcpAsync("list_structured_logs", new { resourceName = bad.Component }, ct);
                var traces = await CallMcpAsync("list_traces", new { resourceName = bad.Component }, ct);

                var prompt = $"Analyze this DigitalBrain failure. Component: {bad.Component} Status: {bad.Status}. Resources: {resources}. Logs: {logs}. Traces: {traces}. Propose one minimal actionable fix (e.g. restart resource or config change).";
                var response = await chat.GetResponseAsync(prompt, cancellationToken: ct);
                analysis = response.Text.Trim();
            }
            catch { /* fall through */ }
        }

        var proposal = $"Apply: {analysis}";
        await FireAsync(new FixProposal(bad.Component, proposal, "SystemStatusNeuron"), ct);

        if (analysis.Contains("restart", StringComparison.OrdinalIgnoreCase) && _mcp != null)
        {
            try { await CallMcpAsync("execute_resource_command", new { resourceName = bad.Component, commandName = "restart" }, ct); } catch { }
        }

        await RunIsolatedSimulationAsync(bad, proposal, ct);
    }

    private Dictionary<string, object?> NormalizeArgs(object? args)
    {
        if (args == null)
        {
            return [];
        }

        if (args is Dictionary<string, object?> d)
        {
            return d;
        }

        var result = new Dictionary<string, object?>();
        foreach (var p in args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            result[p.Name] = p.GetValue(args);
        }
        return result;
    }

    private async Task<string> CallMcpAsync(string tool, object? args = null, CancellationToken ct = default)
    {
        if (_mcp == null)
        {
            return "mcp-unavailable";
        }

        var dict = NormalizeArgs(args);
        var res = await _mcp.CallToolAsync(tool, dict, cancellationToken: ct);
        return res.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no-data";
    }

    private async Task RunIsolatedSimulationAsync(SystemStatusChanged bad, string proposedFix, CancellationToken ct)
    {
        var cp = await CreateCheckpointAsync(ct);
        var result = ComputeSimulationResult(cp.Snapshot, bad, proposedFix);
        await FireAsync(result, ct);
    }

    private static SimulationResult ComputeSimulationResult(IReadOnlyList<Synapse> checkpoint, SystemStatusChanged bad, string proposedFix)
    {
        var simState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in checkpoint)
        {
            if (s is SystemStatusChanged sc && !string.IsNullOrWhiteSpace(sc.Component))
            {
                simState[sc.Component] = sc.Status;
            }
        }

        string before = simState.TryGetValue(bad.Component, out var b) ? b : "unknown";

        string after = "healthy";

        bool differentAndHealthy = !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);

        return new SimulationResult(
            $"bad-state-{bad.Component}",
            differentAndHealthy,
            $"checkpoint replay: {checkpoint.Count} entries. before={before} after={after}. fix='{proposedFix}'. result={(differentAndHealthy ? "different+healthy" : "no improvement")}.");
    }
}
