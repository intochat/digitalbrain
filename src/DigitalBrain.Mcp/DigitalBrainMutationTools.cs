using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Core;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

using DigitalBrain.Ui.Contracts;

// Mutating DigitalBrain MCP tools: fire side-effecting synapses, spend LLM tokens, or change cluster
// state. Registered on trusted local stdio clients and on the dedicated local Aspire MCP host.
[McpServerToolType]
public sealed class DigitalBrainMutationTools(IGrainFactory grains) : DigitalBrainToolsBase(grains)
{
    [McpServerTool(Name = "ask_llm_neuron"), Description("Ask the LLM neuron (powered by the configured local/cloud model) a question or prompt. Returns the response. Requires the kernel cluster and LLM provider to be running.")]
    public async Task<string> AskLlmNeuron(
        [Description("The prompt or question to send to the LLM neuron")] string prompt,
        [Description("Optional preferred model, e.g. 'llama3.1:8b'")] string? preferredModel = null,
        CancellationToken cancellationToken = default)
    {
        var llm = Grains.GetGrain<ILlmNeuron>("llm-main");
        await llm.FireAsync(new LlmPrompt(prompt, preferredModel), cancellationToken);

        var response = (await llm.GetTimelineAsync(cancellationToken)).OfType<LlmResponse>().LastOrDefault();
        return response is not null
            ? $"LLM Response (model: {response.ModelUsed}):\n{response.Response}"
            : "Prompt fired to the LLM neuron, but no response is on the timeline yet (is Ollama running?).";
    }

    [McpServerTool(Name = "fire_synapse"), Description("Fire a generic signal to any neuron by ID. Returns confirmation.")]
    public async Task<string> FireSynapse(
        [Description("Neuron ID / grain key, e.g. 'ino-main', 'llm-main', 'automation-main'")] string neuronId,
        [Description("The text or payload for the synapse (for DemoMessageSynapse)")] string text,
        CancellationToken cancellationToken = default)
    {
        var neuron = ResolveNeuron(neuronId);
        // DemoMessageSynapse removed as trash (Demo projects deleted). Using generic signal for demo compatibility.
        await neuron.FireAsync(new Signal("DemoMessage", new Dictionary<string, object?> { ["text"] = text }), cancellationToken);
        return $"Successfully fired demo signal with text '{text}' to neuron '{neuronId}'.";
    }

    [McpServerTool(Name = "simulate_x_post"), Description("Simulate a new X (Twitter) post from an author, for demo/testing automations that react to XPostReceived. No real X API call is made.")]
    public async Task<string> SimulateXPost(
        [Description("X handle/author of the simulated post, e.g. 'elon'")] string author,
        [Description("Post text")] string text,
        [Description("Telegram chat id to notify if a reactive automation replies")] long chatId,
        CancellationToken cancellationToken = default)
    {
        var ingress = Grains.GetGrain<IIngressNeuron>("ingress-main");
        await ingress.IngestAsync("XPostReceived",
            new Dictionary<string, object?> { ["author"] = author, ["text"] = text, ["chatId"] = chatId },
            cancellationToken);
        return $"Simulated X post from '{author}' broadcast as XPostReceived (chatId {chatId}).";
    }

    [McpServerTool(Name = "ino_interact"), Description(@"Primary structured interaction with INO using the common InoInteractResult contract.

This is the recommended way for external agents (Claude Code, Codex, Grok CLI, tests) to talk to INO and verify system behavior live.

- Uses the full new architecture (direct answers, intent classifier, scoped journals, automation proposals as apps, self-evo rail).
- Returns ResponseText (the actual answer), ClassifiedIntent, AvailableActions (Run/Approve buttons etc.), PendingProposals, memories.
- Always pass a stable client_id for isolated verification sessions.

Use this + ino_list_proposals + ino_approve_proposal for full create/approve/run loops.")]
    public async Task<string> InoInteract(
        [Description("The prompt to INO")] string prompt,
        [Description("Stable client/actor id for scoping and verification (use different ones for different test scenarios)")] string client_id = "mcp-agent",
        [Description("Workspace")] string? workspace_id = null,
        [Description("Include proposal and action data")] bool include_proposals = true,
        CancellationToken cancellationToken = default)
    {
        var ino = Grains.GetGrain<IInoNeuron>("ino-main");
        var req = new InoInteractRequest(prompt, client_id, workspace_id, include_proposals, true);
        var result = await ino.InteractAsync(req, cancellationToken);
        return JsonSerializer.Serialize(result, SurfaceJsonOptions);
    }

    // Legacy thin wrapper kept for compatibility
    [McpServerTool(Name = "ask_ino"), Description("Simple string version of ino_interact. Prefer ino_interact for rich verification.")]
    public async Task<string> AskIno(string prompt, string client_id = "mcp-default", string? workspace_id = null, CancellationToken cancellationToken = default)
    {
        var resultJson = await InoInteract(prompt, client_id, workspace_id, cancellationToken: cancellationToken);
        // Extract just the text for simple callers
        using var doc = JsonDocument.Parse(resultJson);
        if (doc.RootElement.TryGetProperty("ResponseText", out var txt))
        {
            return txt.GetString() ?? resultJson;
        }

        return resultJson;
    }

    [McpServerTool(Name = "ino_list_proposals"), Description("List recent staged SelfEvolutionProposals for automation changes. Essential for testing the approval rail after creating automations via INO.")]
    public async Task<string> InoListProposals([Description("Optional client scope")] string client_id = "mcp-default", CancellationToken cancellationToken = default)
    {
        var selfEvo = Grains.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        var pending = (await selfEvo.GetTimelineAsync(cancellationToken)).OfType<SelfEvolutionProposalPending>().TakeLast(5);
        return JsonSerializer.Serialize(pending.Select(p => new { p.ProposalId, p.ApplyVia, p.Risk }), SurfaceJsonOptions);
    }

    [McpServerTool(Name = "ino_approve_proposal"), Description("Approve a staged proposal (SelfEvolutionDecision). Completes the 'INO creates automation → human/agent approves → activated' flow from the new architecture.")]
    public async Task<string> InoApproveProposal(
        [Description("The proposal id returned from ask_ino or ino_list_proposals")] string proposal_id,
        [Description("Who is approving (for audit)")] string decided_by = "mcp-agent",
        [Description("Client id for context")] string client_id = "mcp-default",
        CancellationToken cancellationToken = default)
    {
        var selfEvo = Grains.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await selfEvo.DeliverAsync(new SelfEvolutionDecision(proposal_id, Approved: true, DecidedBy: decided_by, Reason: "Approved via MCP by external agent"), cancellationToken);
        return $"Approved proposal {proposal_id} as {decided_by}. Check automation list or timeline for activation.";
    }

    [McpServerTool(Name = "ino_list_automations"), Description("List active automations/reactions (the 'apps' INO can create and run).")]
    public async Task<string> InoListAutomations(CancellationToken cancellationToken = default)
    {
        // Reuse the existing good implementation
        return await ListAutomations(cancellationToken);
    }

    [McpServerTool(Name = "ino_show_gallery"), Description("Trigger INO to deliver the live UiKit component gallery surface. Great for testing the gallery path in the new architecture.")]
    public async Task<string> InoShowGallery([Description("Client id")] string client_id = "mcp-default", CancellationToken cancellationToken = default)
    {
        var ino = Grains.GetGrain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("uikit gallery", client_id), cancellationToken);
        return "Gallery intent fired to INO. The surface should have been emitted (check get_workbench_surfaces or UI).";
    }

    [McpServerTool(Name = "ino_get_status"), Description("Quick status of INO + key system parts (recent activity, connected concepts). Useful for agents to understand current brain state before acting.")]
    public async Task<string> InoGetStatus([Description("Client id for scoped view")] string client_id = "mcp-default", CancellationToken cancellationToken = default)
    {
        var ino = Grains.GetGrain<IInoNeuron>("ino-main");
        var tl = await ino.GetOutgoingTimelineAsync(cancellationToken);

        var lastResponses = tl.OfType<InoResponse>().TakeLast(3).Select(r => r.Response.Substring(0, Math.Min(120, r.Response.Length)));
        var mems = tl.OfType<MemorySummary>().TakeLast(3).Select(m => m.Topic);

        return JsonSerializer.Serialize(new
        {
            last_responses = lastResponses,
            recent_memory = mems,
            note = "INO is the central personal assistant. Use ask_ino for most interactions."
        }, SurfaceJsonOptions);
    }

    [McpServerTool(Name = "update_context_filter"), Description("Update ContextNeuron (e.g. when a UI filter changes so INO sees it).")]
    public async Task<string> UpdateContextFilter(
        [Description("Filter/view name")] string view,
        [Description("Filter key")] string filter,
        [Description("Value")] string val,
        CancellationToken cancellationToken = default)
    {
        // IContextNeuron lives in the Ino integration project; Mcp.Tools deliberately avoids that
        // ProjectReference (see DigitalBrainToolsBase.ResolveNeuron's IIngressNeuron comment), so this
        // stays typed as the base INeuron with the literal key rather than pulling in a new dependency edge.
        var context = Grains.GetGrain<INeuron>("context-main");
        await context.FireAsync(new ContextUpdate("filter:" + view, filter, val), cancellationToken);
        await context.FireAsync(new FilterChanged(view, filter, val), cancellationToken); // notify for LLM awareness
        return $"Context+Filter updated for {view}. INO/Context now aware.";
    }

    [McpServerTool(Name = "db_example"), Description("Exercise the DbSupportNeuron (connect + typed query via synapses).")]
    public async Task<string> DbExample([Description("Connection name e.g. northwind")] string name, [Description("Query")] string query, CancellationToken cancellationToken = default)
    {
        var db = Grains.GetGrain<IDbSupportNeuron>(IDbSupportNeuron.SingletonKey);
        await db.FireAsync(new DbConnect(name, "sqlite", "Data Source=:memory:"), cancellationToken);
        await db.FireAsync(new DbQuery(name, query), cancellationToken);
        return "DB neuron handled connect+query via typed synapses. Check timeline for results.";
    }

    [McpServerTool(Name = "cluster_3d_activity"), Description("Fire activity for the 3D graph in the UI kit (connects to cluster observation).")]
    public async Task<string> Cluster3D(
        [Description("Node ID")] string node,
        [Description("Activity type")] string activity,
        [Description("Value")] double value,
        CancellationToken cancellationToken = default)
    {
        var vis = ResolveNeuron("cluster-vis");
        await vis.FireAsync(new ClusterActivity(node, activity, value), cancellationToken);
        await vis.FireAsync(new ThreeDGraphUpdate("main", JsonSerializer.Serialize(new { node, activity, value })), cancellationToken);
        return "Cluster activity sent for 3D visualization.";
    }

    [McpServerTool(Name = "define_reaction"), Description("Stage a reactive automation for approval. when=NeuronActivated|Signal:Foo|*, script is real executable C# and is not activated until a SelfEvolutionDecision approves it.")]
    public async Task<string> DefineReaction(
        [Description("Unique id for the reaction")] string id,
        [Description("Condition e.g. 'NeuronActivated' or 'Signal:MySignal'")] string when,
        [Description("Optional target neuron key (e.g. 'personal-assistant') or null for any")] string? target,
        [Description("The C# script body (real executable C#; supports return [...] and await Fire)")] string scriptCode,
        [Description("Optional scope for multi-user (default='default' = global)")] string scope = "default",
        CancellationToken cancellationToken = default)
    {
        var proposalId = await StageAutomationDefinitionAsync(id, when, target, scriptCode, scope, "define_reaction", cancellationToken);
        return $"Staged reaction '{id}' for approval as proposal '{proposalId}' (when={when}, target={target ?? "any"}, scope={scope}).";
    }

    private async Task<string> StageAutomationDefinitionAsync(
        string id,
        string when,
        string? target,
        string scriptCode,
        string scope,
        string source,
        CancellationToken cancellationToken = default)
    {
        const string automationNeuronId = "automation-main";
        var proposalId = "automation-" + Guid.NewGuid().ToString("N");
        var scriptId = id + "-script";
        var script = new RegisterScript(scriptId, scriptCode, $"via-mcp:{source}", Array.Empty<string>(), scope);
        var reaction = new RegisterReaction(id, when, scriptId, target, Array.Empty<string>(), scope, null);

        var auto = Grains.GetGrain<IAutomationNeuron>(automationNeuronId);
        await auto.FireAsync(new AutomationDefinitionStaged(proposalId, automationNeuronId, script, reaction), cancellationToken);

        var approval = Grains.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionProposal(
            ProposalId: proposalId,
            Scope: $"automation:{scope}",
            Rationale: $"MCP {source}: define reaction {id} when {when}.",
            ProposedChange: $"Register script {scriptId} and reaction {id} targeting {target ?? "any"}.",
            ApplyVia: SelfEvolutionApplyVia.AutomationDefineReaction,
            Risk: SelfEvolutionRisk.InProcessCode,
            RequiresHumanApproval: true,
            RollbackPlan: $"Remove reaction {id} and script {scriptId} if approval apply fails verification.",
            Origin: automationNeuronId)
        {
            Receiver = new NeuronId(SelfEvolutionNeuronIds.Main),
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        return proposalId;
    }
    [McpServerTool(Name = "list_automations"), Description("List currently active reactions and scripts (surface-friendly). Use define_reaction or create_automation_from_description to add. Supports script reuse by id.")]
    public async Task<string> ListAutomations(CancellationToken cancellationToken = default)
    {
        // IAutomationNeuron's list/library methods have no cancellable overload today; the parameter is
        // still accepted so the MCP SDK can bind and honor notifications/cancelled at the tool boundary.
        var auto = Grains.GetGrain<IAutomationNeuron>("automation-main");
        // Query triggers fresh surface emission (AutomationSurface + ListSurface) for UI/HomeFeed
        var reactions = await auto.ListActiveReactionsAsync();
        var scripts = await auto.ListActiveScriptsAsync();
        var details = new System.Text.StringBuilder();
        details.AppendLine("Active reactions:");
        foreach (var r in reactions)
        {
            details.AppendLine($"  - {r}");
        }
        details.AppendLine("Active scripts (reusable by id):");
        foreach (var s in scripts)
        {
            var code = await auto.GetScriptCodeAsync(s);
            details.AppendLine($"  - {s}: {(code?.Length > 50 ? code.Substring(0, 50) + "..." : code)}");
        }
        // Rich library view (priority 4)
        var lib = await auto.ListScriptLibraryAsync();
        if (lib.Count > 0)
        {
            details.AppendLine("Script library entries (id, usage):");
            foreach (var e in lib)
            {
                details.AppendLine($"  - {e.Id} (uses:{e.UsageCount}) desc:{e.Description}");
            }
        }
        details.AppendLine("(AutomationSurface emitted to timeline for UI consumption.)");
        return details.ToString();
    }

    [McpServerTool(Name = "remove_reaction"), Description("Stage removal of a reaction by id. The reaction is not removed until a SelfEvolutionDecision approves the proposal.")]
    public async Task<string> RemoveReaction([Description("Reaction id to remove")] string id, CancellationToken cancellationToken = default)
    {
        const string automationNeuronId = "automation-main";
        var proposalId = "automation-remove-" + Guid.NewGuid().ToString("N");
        var auto = Grains.GetGrain<IAutomationNeuron>(automationNeuronId);
        await auto.FireAsync(new AutomationRemovalStaged(proposalId, automationNeuronId, id), cancellationToken);

        var approval = Grains.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionProposal(
            ProposalId: proposalId,
            Scope: "automation:default",
            Rationale: $"MCP remove_reaction requested removal of reaction {id}.",
            ProposedChange: $"Remove reaction {id}.",
            ApplyVia: SelfEvolutionApplyVia.AutomationRemoveReaction,
            Risk: SelfEvolutionRisk.InProcessCode,
            RequiresHumanApproval: true,
            RollbackPlan: "Re-register the removed reaction from journaled automation history if removal was incorrect.",
            Origin: automationNeuronId)
        {
            Receiver = new NeuronId(SelfEvolutionNeuronIds.Main),
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        return $"Staged removal of reaction '{id}' as proposal '{proposalId}'. Approve it with ino_approve_proposal before it is removed.";
    }

    [McpServerTool(Name = "create_automation_from_description"), Description("High-level sugar for Ino/LLM: describe in English and stage a conservative automation proposal for approval.")]
    public async Task<string> CreateAutomationFromDescription(
        [Description("Natural language description of the when-then automation")] string description,
        [Description("Optional explicit id, otherwise derived")] string? id = null,
        CancellationToken cancellationToken = default)
    {
        var reactionId = string.IsNullOrWhiteSpace(id)
            ? "auto-" + Guid.NewGuid().ToString("N")[..10]
            : id.Trim();
        var descriptionLiteral = JsonSerializer.Serialize(description);
        var scriptCode =
            "return new[] { new Signal(\"AutomationRequested\", new Dictionary<string, object?> " +
            "{ [\"description\"] = " + descriptionLiteral + ", [\"source\"] = \"mcp\" }) };";

        var proposalId = await StageAutomationDefinitionAsync(
            reactionId,
            "*",
            null,
            scriptCode,
            "default",
            "create_automation_from_description",
            cancellationToken);

        return $"Staged automation '{reactionId}' from description as proposal '{proposalId}'. Approve it with ino_approve_proposal before it becomes active.";
    }

    [McpServerTool(Name = "visualize_data"), Description("Infer a generic data-chart UiSurface from JSON rows and return the generated surface JSON. The Flutter UI renders this dynamically by UiSurface.kind.")]
    public async Task<string> VisualizeData(
        [Description("Prompt describing what chart the user wants")] string prompt,
        [Description("JSON array of row objects, or an object containing rows/data/items")] string dataJson,
        [Description("Optional chart hint: bar, line, area, scatter, or pie")] string? chartHint = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = "chart-" + Guid.NewGuid().ToString("N")[..10];
        var chart = Grains.GetGrain<IDataVisualizationNeuron>("chart-main");
        await chart.FireAsync(new VisualizeDataRequest(prompt, dataJson, chartHint, requestId), cancellationToken);

        var timeline = await chart.GetTimelineAsync(cancellationToken);
        var failed = timeline.OfType<DataChartFailed>().LastOrDefault(result => result.RequestId == requestId);
        if (failed is not null)
        {
            return $"Data chart generation failed: {failed.Reason}";
        }

        var generated = timeline.OfType<DataChartGenerated>().LastOrDefault(result => result.RequestId == requestId);
        return generated is null
            ? $"VisualizeDataRequest accepted as {requestId}, but no chart result was found yet."
            : JsonSerializer.Serialize(generated.Surface, SurfaceJsonOptions);
    }

    [McpServerTool(Name = "fire_ui_action"), Description("Execute a UiSurface action descriptor by mapping synapseType and props to existing DigitalBrain command contracts.")]
    public async Task<string> FireUiAction(
        [Description("Action descriptor JSON with actionId, label, synapseType, and props")] string actionJson,
        [Description("Fallback neuron id for generic/demo actions")] string defaultNeuronId = "ino-main",
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(actionJson);
        var action = document.RootElement;
        var synapseType = ReadString(action, UiSurfaceKeys.SynapseType);
        if (string.IsNullOrWhiteSpace(synapseType))
        {
            return "Action descriptor missing synapseType.";
        }

        var props = ReadObject(action, UiSurfaceKeys.Props);

        switch (synapseType)
        {
            case "RunKernelTask":
                {
                    // UI action string kept as "RunKernelTask" for surface compat; message type is now the generic core protocol
                    var taskId = ReadString(props, "taskId") ?? "task-" + Guid.NewGuid().ToString("N")[..8];
                    var description = ReadString(props, "description") ?? ReadString(props, "prompt") ?? "Run task";
                    await Grains.GetGrain<INeuron>(taskId).FireAsync(new RunTask(taskId, description), cancellationToken);
                    return $"Fired RunTask for {taskId}.";
                }
            case "CancelKernelTask":
                {
                    var taskId = ReadString(props, "taskId");
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        return "CancelTask action requires props.taskId.";
                    }

                    await Grains.GetGrain<INeuron>(taskId).FireAsync(new CancelTask(taskId), cancellationToken);
                    return $"Fired CancelTask for {taskId}.";
                }
            case nameof(InoRequest):
                {
                    var prompt = ReadString(props, "prompt") ?? ReadString(props, "text");
                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        return "InoRequest action requires props.prompt.";
                    }

                    var sessionId = ReadString(props, "sessionId");
                    await Grains.GetGrain<IInoNeuron>("ino-main").FireAsync(new InoRequest(prompt, sessionId), cancellationToken);
                    return "Fired InoRequest.";
                }
            case nameof(RestartResource):
                {
                    var resourceName = ReadString(props, "resourceName");
                    if (string.IsNullOrWhiteSpace(resourceName))
                    {
                        return "RestartResource action requires props.resourceName.";
                    }

                    await Grains.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new RestartResource(resourceName), cancellationToken);
                    return $"Fired RestartResource for {resourceName}.";
                }
            default:
                {
                    var target = ReadString(props, "neuronId") ?? defaultNeuronId;
                    await ResolveNeuron(target).FireAsync(new Signal("DemoMessage", new Dictionary<string, object?> { ["payload"] = actionJson }), cancellationToken);
                    return $"Forwarded unrecognized UI action '{synapseType}' to {target} as generic signal.";
                }
        }
    }

}


