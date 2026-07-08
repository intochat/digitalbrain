using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace DigitalBrain.Mcp;

using DigitalBrain.Ui.Contracts;

// Mutating DigitalBrain MCP tools: fire side-effecting synapses, spend LLM tokens, or change marketplace/cluster
// state. Registered on trusted local stdio clients and on the dedicated local Aspire MCP host.
[McpServerToolType]
public sealed class DigitalBrainMutationTools(IGrainFactory grains) : DigitalBrainToolsBase(grains)
{
    [McpServerTool(Name = "ask_llm_neuron"), Description("Ask the LLM neuron (powered by local Qwen/Ollama) a question or prompt. Returns the response. Requires the kernel cluster and Ollama to be running.")]
    public async Task<string> AskLlmNeuron(
        [Description("The prompt or question to send to the LLM neuron")] string prompt,
        [Description("Optional preferred model, e.g. 'qwen2.5-coder:1.5b'")] string? preferredModel = null)
    {
        var llm = Grains.GetGrain<ILlmNeuron>("llm-main");
        await llm.FireAsync(new LlmPrompt(prompt, preferredModel));

        var response = (await llm.GetTimelineAsync()).OfType<LlmResponse>().LastOrDefault();
        return response is not null
            ? $"LLM Response (model: {response.ModelUsed}):\n{response.Response}"
            : "Prompt fired to the LLM neuron, but no response is on the timeline yet (is Ollama running?).";
    }

    [McpServerTool(Name = "fire_synapse"), Description("Fire a synapse (message) to any neuron by ID. Use for demo, system, marketplace etc. Returns confirmation.")]
    public async Task<string> FireSynapse(
        [Description("Neuron ID / grain key, e.g. 'demo-opt', 'llm-main', 'market-main'")] string neuronId,
        [Description("The text or payload for the synapse (for DemoMessageSynapse)")] string text)
    {
        var neuron = ResolveNeuron(neuronId);
        // DemoMessageSynapse removed as trash (Demo projects deleted). Using generic signal for demo compatibility.
        await neuron.FireAsync(new Signal("DemoMessage", new Dictionary<string, object?> { ["text"] = text }));
        return $"Successfully fired demo signal with text '{text}' to neuron '{neuronId}'.";
    }

    [McpServerTool(Name = "simulate_x_post"), Description("Simulate a new X (Twitter) post from an author, for demo/testing automations that react to XPostReceived. No real X API call is made.")]
    public async Task<string> SimulateXPost(
        [Description("X handle/author of the simulated post, e.g. 'elon'")] string author,
        [Description("Post text")] string text,
        [Description("Telegram chat id to notify if a reactive automation replies")] long chatId)
    {
        var ingress = Grains.GetGrain<IIngressNeuron>("ingress-main");
        await ingress.IngestAsync("XPostReceived",
            new Dictionary<string, object?> { ["author"] = author, ["text"] = text, ["chatId"] = chatId });
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
        [Description("Include proposal and action data")] bool include_proposals = true)
    {
        var ino = Grains.GetGrain<IInoNeuron>("ino-main");
        var req = new InoInteractRequest(prompt, client_id, workspace_id, include_proposals, true);
        var result = await ino.InteractAsync(req);
        return JsonSerializer.Serialize(result, SurfaceJsonOptions);
    }

    // Legacy thin wrapper kept for compatibility
    [McpServerTool(Name = "ask_ino"), Description("Simple string version of ino_interact. Prefer ino_interact for rich verification.")]
    public async Task<string> AskIno(string prompt, string client_id = "mcp-default", string? workspace_id = null)
    {
        var resultJson = await InoInteract(prompt, client_id, workspace_id);
        // Extract just the text for simple callers
        using var doc = JsonDocument.Parse(resultJson);
        if (doc.RootElement.TryGetProperty("ResponseText", out var txt))
            return txt.GetString() ?? resultJson;
        return resultJson;
    }

    [McpServerTool(Name = "ino_list_proposals"), Description("List recent staged SelfEvolutionProposals (automations, packs, code changes). Essential for testing the approval rail after creating automations via INO.")]
    public async Task<string> InoListProposals([Description("Optional client scope")] string client_id = "mcp-default")
    {
        var selfEvo = Grains.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        var pending = (await selfEvo.GetTimelineAsync()).OfType<SelfEvolutionProposalPending>().TakeLast(5);
        return JsonSerializer.Serialize(pending.Select(p => new { p.ProposalId, p.ApplyVia, p.Risk }), SurfaceJsonOptions);
    }

    [McpServerTool(Name = "ino_approve_proposal"), Description("Approve a staged proposal (SelfEvolutionDecision). Completes the 'INO creates automation → human/agent approves → activated' flow from the new architecture.")]
    public async Task<string> InoApproveProposal(
        [Description("The proposal id returned from ask_ino or ino_list_proposals")] string proposal_id,
        [Description("Who is approving (for audit)")] string decided_by = "mcp-agent",
        [Description("Client id for context")] string client_id = "mcp-default")
    {
        var selfEvo = Grains.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await selfEvo.DeliverAsync(new SelfEvolutionDecision(proposal_id, Approved: true, DecidedBy: decided_by, Reason: "Approved via MCP by external agent"));
        return $"Approved proposal {proposal_id} as {decided_by}. Check automation list or timeline for activation.";
    }

    [McpServerTool(Name = "ino_list_automations"), Description("List active automations/reactions (the 'apps' INO can create and run).")]
    public async Task<string> InoListAutomations()
    {
        // Reuse the existing good implementation
        return await ListAutomations();
    }

    [McpServerTool(Name = "ino_show_gallery"), Description("Trigger INO to deliver the live UiKit component gallery surface. Great for testing the gallery path in the new architecture.")]
    public async Task<string> InoShowGallery([Description("Client id")] string client_id = "mcp-default")
    {
        var ino = Grains.GetGrain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("uikit gallery", client_id));
        return "Gallery intent fired to INO. The surface should have been emitted (check get_workbench_surfaces or UI).";
    }

    [McpServerTool(Name = "ino_get_status"), Description("Quick status of INO + key system parts (recent activity, connected concepts). Useful for agents to understand current brain state before acting.")]
    public async Task<string> InoGetStatus([Description("Client id for scoped view")] string client_id = "mcp-default")
    {
        var ino = Grains.GetGrain<IInoNeuron>("ino-main");
        var tl = await ino.GetOutgoingTimelineAsync();

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
        [Description("Value")] string val)
    {
        var context = Grains.GetGrain<INeuron>("context-main");
        await context.FireAsync(new ContextUpdate("filter:" + view, filter, val));
        await context.FireAsync(new FilterChanged(view, filter, val)); // notify for LLM awareness
        return $"Context+Filter updated for {view}. INO/Context now aware.";
    }

    [McpServerTool(Name = "db_example"), Description("Exercise the DbSupportNeuron (connect + typed query via synapses).")]
    public async Task<string> DbExample([Description("Connection name e.g. northwind")] string name, [Description("Query")] string query)
    {
        var db = Grains.GetGrain<IDbSupportNeuron>("db-main");
        await db.FireAsync(new DbConnect(name, "sqlite", "Data Source=:memory:"));
        await db.FireAsync(new DbQuery(name, query));
        return "DB neuron handled connect+query via typed synapses. Check timeline for results.";
    }

    [McpServerTool(Name = "cluster_3d_activity"), Description("Fire activity for the 3D graph in the UI kit (connects to cluster observation).")]
    public async Task<string> Cluster3D(
        [Description("Node ID")] string node,
        [Description("Activity type")] string activity,
        [Description("Value")] double value)
    {
        var vis = ResolveNeuron("cluster-vis");
        await vis.FireAsync(new ClusterActivity(node, activity, value));
        await vis.FireAsync(new ThreeDGraphUpdate("main", JsonSerializer.Serialize(new { node, activity, value })));
        return "Cluster activity sent for 3D visualization.";
    }

    [McpServerTool(Name = "define_reaction"), Description("Stage a reactive automation for approval. when=NeuronActivated|Signal:Foo|*, script is real executable C# and is not activated until a SelfEvolutionDecision approves it.")]
    public async Task<string> DefineReaction(
        [Description("Unique id for the reaction")] string id,
        [Description("Condition e.g. 'NeuronActivated' or 'Signal:MySignal'")] string when,
        [Description("Optional target neuron key (e.g. 'personal-assistant') or null for any")] string? target,
        [Description("The C# script body (real executable C#; supports return [...] and await Fire)")] string scriptCode,
        [Description("Optional scope for multi-user (default='default' = global)")] string scope = "default")
    {
        var proposalId = await StageAutomationDefinitionAsync(id, when, target, scriptCode, scope, "define_reaction");
        return $"Staged reaction '{id}' for approval as proposal '{proposalId}' (when={when}, target={target ?? "any"}, scope={scope}).";
    }

    private async Task<string> StageAutomationDefinitionAsync(
        string id,
        string when,
        string? target,
        string scriptCode,
        string scope,
        string source)
    {
        const string automationNeuronId = "automation-main";
        var proposalId = "automation-" + Guid.NewGuid().ToString("N");
        var scriptId = id + "-script";
        var script = new RegisterScript(scriptId, scriptCode, $"via-mcp:{source}", Array.Empty<string>(), scope);
        var reaction = new RegisterReaction(id, when, scriptId, target, Array.Empty<string>(), scope, null);

        var auto = Grains.GetGrain<IAutomationNeuron>(automationNeuronId);
        await auto.FireAsync(new AutomationDefinitionStaged(proposalId, automationNeuronId, script, reaction));

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
        });

        return proposalId;
    }
    [McpServerTool(Name = "list_automations"), Description("List currently active reactions and scripts (surface-friendly). Use define_reaction or create_automation_from_description to add. Supports script reuse by id.")]
    public async Task<string> ListAutomations()
    {
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
            foreach (var e in lib) details.AppendLine($"  - {e.Id} (uses:{e.UsageCount}) desc:{e.Description}");
        }
        details.AppendLine("(AutomationSurface emitted to timeline for UI consumption.)");
        return details.ToString();
    }

    [McpServerTool(Name = "remove_reaction"), Description("Remove a reaction by id.")]
    public async Task<string> RemoveReaction([Description("Reaction id to remove")] string id)
    {
        var auto = Grains.GetGrain<IAutomationNeuron>("automation-main");
        await auto.RemoveReactionAsync(id);
        return $"Removed reaction {id}.";
    }

    [McpServerTool(Name = "create_automation_from_description"), Description("High-level sugar for Ino/LLM: describe in English like 'when personal-assistant activates then emit DailyBriefGenerated with name'. Internally creates real RegisterScript + Reaction using DefineReactionAsync. Returns confirmation.")]
    public async Task<string> CreateAutomationFromDescription(
        [Description("Natural language description of the when-then automation")] string description,
        [Description("Optional explicit id, otherwise derived")] string? id = null)
    {
        // Wired to Foundry (P2): intent -> generated script + RegisterReaction (trigger + caps manifest) -> gate -> proposal with preview.
        var loop = Grains.GetGrain<ICodeFoundryLoopNeuron>("foundry-main");
        await loop.FireAsync(new FoundryRequest(
            $"Produce C# script and RegisterReaction payload (include Schedule/Poll trigger + caps from ICapabilityBroker manifest e.g. Http/Llm) for: {description}. Use approval rail.",
            TargetTier.Run,
            AutoApply: false));
        var idPart = id != null ? $" (id={id})" : "";
        return $"Foundry LLM rail wired for '{description}'{idPart}. Proposal staged for approval (check timeline for diff/preview).";
    }

    [McpServerTool(Name = "run_closed_loop"), Description("Trigger a marketplace closed loop ('ui' for Dart MCP widget-tree authoring, 'se' for SoftwareEngineering runtime mod via Aspire MCP + LLM).")]
    public async Task<string> RunClosedLoop(
        [Description("Loop type: ui | se")] string loopType,
        [Description("Prompt or task for the loop, e.g. inspect editor tree and improve")] string prompt)
    {
        var loop = Grains.GetGrain<IClosedLoopNeuron>("closedloop-main");
        await loop.FireAsync(new ClosedLoopRequest(loopType, prompt));
        return $"ClosedLoop {loopType} triggered on the marketplace-installed experience.";
    }

    [McpServerTool(Name = "dart_ui_inspect_and_reload"), Description("Helper for the UI closed loop: connect Dart DTD, get the live widget tree, and hot reload after mods.")]
    public static string DartUIInspect(
        [Description("DTD uri from running flutter (copy from IDE or console)")] string dtdUri,
        [Description("Whether to hot reload after inspect")] bool doReload = false)
        => $"[UIClosedLoop] Connect dart DTD {dtdUri}, call get_widget_tree(summaryOnly=true), then hot_reload after the LLM proposes edits (doReload={doReload}).";

    [McpServerTool(Name = "run_code_foundry")]
    [Description("Generate, compile, and (Run) execute in-process or (Deploy) build+restart a new neuron. tier is 'Run' or 'Deploy'.")]
    public async Task<string> RunCodeFoundry(
        [Description("English spec of the code to generate")] string spec,
        [Description("'Run' for Tier-1 in-process, 'Deploy' for Tier-2 durable")] string tier = "Run",
        [Description("Apply automatically (requires trusted kernel config; default stages approval only)")] bool autoApply = false)
    {
        var parsedTier = string.Equals(tier, "Deploy", StringComparison.OrdinalIgnoreCase)
            ? TargetTier.Deploy
            : TargetTier.Run;

        var loop = Grains.GetGrain<ICodeFoundryLoopNeuron>("foundry-main");
        await loop.FireAsync(new FoundryRequest(spec, parsedTier, autoApply));

        var timeline = await loop.GetOutgoingTimelineAsync();
        var terminal = timeline.LastOrDefault(s =>
            s.Type == nameof(FoundryCompleted) || s.Type == nameof(FoundryRolledBack));
        return terminal?.Type ?? "FoundryRequest accepted (no terminal synapse yet).";
    }

    [McpServerTool(Name = "visualize_data"), Description("Infer a generic data-chart UiSurface from JSON rows and return the generated surface JSON. The Flutter UI renders this dynamically by UiSurface.kind.")]
    public async Task<string> VisualizeData(
        [Description("Prompt describing what chart the user wants")] string prompt,
        [Description("JSON array of row objects, or an object containing rows/data/items")] string dataJson,
        [Description("Optional chart hint: bar, line, area, scatter, or pie")] string? chartHint = null)
    {
        var requestId = "chart-" + Guid.NewGuid().ToString("N")[..10];
        var chart = Grains.GetGrain<IDataVisualizationNeuron>("chart-main");
        await chart.FireAsync(new VisualizeDataRequest(prompt, dataJson, chartHint, requestId));

        var timeline = await chart.GetTimelineAsync();
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
        [Description("Fallback neuron id for generic/demo actions")] string defaultNeuronId = "ino-main")
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
                    await Grains.GetGrain<INeuron>(taskId).FireAsync(new RunTask(taskId, description));
                    return $"Fired RunTask for {taskId}.";
                }
            case "CancelKernelTask":
                {
                    var taskId = ReadString(props, "taskId");
                    if (string.IsNullOrWhiteSpace(taskId)) return "CancelTask action requires props.taskId.";
                    await Grains.GetGrain<INeuron>(taskId).FireAsync(new CancelTask(taskId));
                    return $"Fired CancelTask for {taskId}.";
                }
            case nameof(InoRequest):
                {
                    var prompt = ReadString(props, "prompt") ?? ReadString(props, "text");
                    if (string.IsNullOrWhiteSpace(prompt)) return "InoRequest action requires props.prompt.";
                    var sessionId = ReadString(props, "sessionId");
                    await Grains.GetGrain<IInoNeuron>("ino-main").FireAsync(new InoRequest(prompt, sessionId));
                    return "Fired InoRequest.";
                }
            case nameof(InstallFromMarketplace):
                {
                    var packName = ReadString(props, "packName");
                    var version = ReadString(props, "version") ?? "0.1.0";
                    var buyerId = ReadString(props, "buyerId") ?? "current-user";
                    if (string.IsNullOrWhiteSpace(packName)) return "InstallFromMarketplace action requires props.packName.";
                    await Grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new InstallFromMarketplace(packName, version, buyerId));
                    return $"Fired InstallFromMarketplace for {packName}@{version}.";
                }
            case nameof(ListPublished):
                await Grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new ListPublished());
                return "Fired ListPublished.";
            case nameof(RestartResource):
                {
                    var resourceName = ReadString(props, "resourceName");
                    if (string.IsNullOrWhiteSpace(resourceName)) return "RestartResource action requires props.resourceName.";
                    await Grains.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new RestartResource(resourceName));
                    return $"Fired RestartResource for {resourceName}.";
                }
            case nameof(ClosedLoopRequest):
                {
                    var loopType = ReadString(props, "loopType") ?? "ui";
                    var prompt = ReadString(props, "prompt") ?? "Run installed closed loop";
                    await Grains.GetGrain<IClosedLoopNeuron>("closedloop-main").FireAsync(new ClosedLoopRequest(loopType, prompt));
                    return $"Fired ClosedLoopRequest for {loopType}.";
                }
            default:
                {
                    var target = ReadString(props, "neuronId") ?? defaultNeuronId;
                    await ResolveNeuron(target).FireAsync(new Signal("DemoMessage", new Dictionary<string, object?> { ["payload"] = actionJson }));
                    return $"Forwarded unrecognized UI action '{synapseType}' to {target} as generic signal.";
                }
        }
    }

    [McpServerTool(Name = "publish_to_marketplace"), Description("Publish a pack/experience (e.g. generated neuron code) to the marketplace. Supports private and commission rate.")]
    public async Task<string> PublishToMarketplace(
        [Description("Pack name")] string packName,
        [Description("Version, e.g. '0.1-dev'")] string version,
        [Description("The code or content of the pack")] string code,
        [Description("Owner ID")] string ownerId = "mcp-user",
        [Description("Is private pack?")] bool isPrivate = false,
        [Description("Commission rate e.g. 0.15 for 15%")] double commissionRate = 0.15)
    {
        var market = Grains.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(packName, version, code, ownerId, isPrivate, commissionRate));
        return $"Published '{packName}@{version}' to marketplace (private={isPrivate}, commission={commissionRate:P0}).";
    }

    [McpServerTool(Name = "install_from_marketplace"), Description("Install a pack from the marketplace. Triggers commission.")]
    public async Task<string> InstallFromMarketplace(
        [Description("Pack name to install")] string packName,
        [Description("Version")] string version,
        [Description("Buyer ID for commission tracking")] string buyerId = "mcp-buyer")
    {
        var market = Grains.GetGrain<IMarketplaceNeuron>("market-main");
        await GetPublishedPacksWithLocalSeedsAsync(market);
        await market.FireAsync(new InstallFromMarketplace(packName, version, buyerId));
        return $"Installed '{packName}@{version}' for buyer '{buyerId}'. Commission should have been taken.";
    }

    [McpServerTool(Name = "promote_automations_to_pack"), Description("Thin promotion (priority 6): crystallize selected reaction ids + their scripts into a NeuroPack seed stub for the heavy publish/install pipeline. Example: promote 'my-auto' reactions to a named pack.")]
    public async Task<string> PromoteAutomationsToPack(
        [Description("Pack name for the crystallized output")] string packName,
        [Description("Version e.g. 0.1.0")] string version,
        [Description("Comma separated reaction ids to include")] string reactionIdsCsv,
        [Description("Optional owner")] string ownerId = "automation-user")
    {
        var auto = Grains.GetGrain<IAutomationNeuron>("automation-main");
        var ids = reactionIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        await auto.PromoteToPackAsync(packName, version, ids, ownerId);
        return $"Promotion requested for {packName}@{version} covering {ids.Count} reactions. Watch for AutomationPromoted + crystallized signal.";
    }
}


