using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Kernel;

public class SoftwareEngineeringClosedLoopNeuron(ILogger<SoftwareEngineeringClosedLoopNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IClosedLoopNeuron
{
    private const string AspireInspectionEnabledKey = "DigitalBrain:ClosedLoop:InspectAspireMcp";
    private McpClient? _aspireMcp;

    public async Task HandleAsync(ClosedLoopRequest req, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("ClosedLoop {Type} requested: {Prompt}", req.LoopType, req.Prompt);

        var chat = ServiceProvider.GetService<IChatClient>();
        string analysis = "no-llm-fallback";

        if (chat != null)
        {
            var sysPrompt = req.LoopType.Equals("ui", StringComparison.OrdinalIgnoreCase) || req.LoopType.Contains("dart", StringComparison.OrdinalIgnoreCase)
                ? "You are the UI Closed Loop. Use Dart MCP tools (connect_dart_tooling_daemon with DTD uri, get_widget_tree summaryOnly:true for user code, get_selected_widget, get_runtime_errors, hot_reload, launch_app on sdk/flutter_demo) to inspect live Flutter widget trees while authoring. Propose precise Dart code changes to improve surfaces, skill integration, and editor experiences in the workbench. Output: tree summary, proposed file edits or new widget code, then hot reload command."
                : "You are the SoftwareEngineering ClosedLoopNeuron. Inspect via Aspire MCP (list_resources, list_structured_logs, list_traces), use local context from journals. Propose runtime modifications to neurons, INO, automations, and editor surfaces. Apply by staging a self-evolution proposal or by using Aspire execute_resource_command restart on the kernel resource because multiple kernels may run. Prefer Aspire-orchestrated applies + checkpoints. Be concise.";
            var full = sysPrompt + "\nPROMPT: " + req.Prompt + "\nCTX: journal-driven";
            try
            {
                var response = await chat.GetResponseAsync(full, cancellationToken: cancellationToken);
                var acc = response.Text;
                analysis = string.IsNullOrWhiteSpace(acc) ? "processed" : acc.Trim();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "ClosedLoop LLM analysis failed; recording fallback completion.");
                analysis = "llm-error-fallback: " + ex.GetBaseException().Message;
            }
        }

        await FireAsync(new ClosedLoopCompleted(req.LoopType, analysis.Length > 20 ? analysis : "processed", false), cancellationToken);

        var shouldStageSelfEvolution =
            !req.LoopType.Contains("ui", StringComparison.OrdinalIgnoreCase) &&
            (analysis.Contains("restart", StringComparison.OrdinalIgnoreCase) ||
             analysis.Contains("apply", StringComparison.OrdinalIgnoreCase));

        if (shouldStageSelfEvolution)
        {
            await StageSelfEvolutionProposalAsync(req, analysis, cancellationToken);

            if (!ShouldInspectAspireMcp())
            {
                return;
            }

            await EnsureAspireMcpAsync(cancellationToken);
            if (_aspireMcp != null)
            {
                try
                {
                    var res = await CallAspireMcpAsync("list_resources", ct: cancellationToken);
                    Logger.LogInformation("ClosedLoop staged self-evolution proposal with Aspire resources visible: {Res}", res.Substring(0, Math.Min(200, res.Length)));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch { }
            }
        }
    }

    public async Task HandleAsync(ExperienceUsed used, CancellationToken cancellationToken = default)
    {
        if (used.Pack.Contains("ClosedLoop", StringComparison.OrdinalIgnoreCase) || used.Pack.Contains("UIClosed", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation("ClosedLoop embodied from pack {Pack}", used.Pack);
            await FireAsync(new ClosedLoopRequest(used.Pack.Contains("UI") ? "ui" : "se", "Embodied pack activation: begin closed improvement loop"), cancellationToken);
        }
    }

    private async Task StageSelfEvolutionProposalAsync(ClosedLoopRequest req, string analysis, CancellationToken cancellationToken)
    {
        const string applyVia = "aspire-mcp";

        await FireAsync(new SystemModificationProposed("aspire", "closedloop", analysis, applyVia), cancellationToken);
        var proposal = new SelfEvolutionProposal(
            ProposalId: "closedloop-" + req.SynapseId,
            Scope: "kernel",
            Rationale: $"ClosedLoop {req.LoopType}: {req.Prompt}",
            ProposedChange: analysis,
            ApplyVia: applyVia,
            Risk: SelfEvolutionRisk.KernelRestart,
            RequiresHumanApproval: true,
            RollbackPlan: "Create a checkpoint before apply; use rolling rollback if verification fails.",
            Origin: Self.Value)
        {
            Sender = Self,
            Receiver = new NeuronId(SelfEvolutionNeuronIds.Main),
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = CurrentCause?.CorrelationId ?? CurrentCause?.SynapseId,
            CausationId = CurrentCause?.SynapseId
        };

        await GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main).DeliverAsync(proposal, cancellationToken);
    }

    private bool ShouldInspectAspireMcp()
    {
        var config = ServiceProvider.GetService<IConfiguration>();
        return config?.GetValue(AspireInspectionEnabledKey, true) ?? true;
    }

    private async Task EnsureAspireMcpAsync(CancellationToken cancellationToken)
    {
        if (_aspireMcp != null)
        {
            return;
        }

        try
        {
            var workDir = Directory.GetCurrentDirectory();
            _aspireMcp = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire-closedloop",
                    Command = "aspire",
                    Arguments = ["agent", "mcp"],
                    WorkingDirectory = workDir
                }),
                cancellationToken: cancellationToken);
            await FireAsync(new SystemStatusChanged("closedloop-aspire-mcp", "connected"), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ClosedLoop Aspire MCP connect failed");
        }
    }

    private async Task<string> CallAspireMcpAsync(string tool, object? args = null, CancellationToken ct = default)
    {
        if (_aspireMcp == null)
        {
            return "mcp-unavailable";
        }

        var dict = new Dictionary<string, object?>();
        if (args != null)
        {
            foreach (var p in args.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                dict[p.Name] = p.GetValue(args);
            }
        }
        var res = await _aspireMcp.CallToolAsync(tool, dict, cancellationToken: ct);
        return res.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no-data";
    }
}
