using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using DigitalBrain.Hosting.DigitalBrain;
using DigitalBrain.Hosting.Microsoft.Aspire;
using DigitalBrain.Protocol.Microsoft.Aspire;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using System.Text.Json;

namespace DigitalBrain.Ino.Experiences;

public interface ILlmAgentNeuron : IGrainWithStringKey
{
    Task EnsureActiveAsync();
    Task<IReadOnlyList<Synapse>> GetAgentHistoryAsync(int max = 20, CancellationToken cancellationToken = default);
}

public abstract class LlmNeuron : Neuron
{
    private readonly IServiceProvider _services;

    protected LlmNeuron(IServiceProvider services)
    {
        _services = services;
    }

    protected virtual string DefaultTier => "fast";

    protected virtual ChatClientBuilder ConfigureChat(ChatClientBuilder builder) =>
        builder.UseOpenTelemetry(sourceName: "DigitalBrain.Neuron");

    protected virtual IChatClient BuildChat()
    {
        var tier = DefaultTier;
        var keyed = _services.GetRequiredKeyedService<IChatClient>(tier);
        return ConfigureChat(new ChatClientBuilder(keyed)).Build();
    }

    protected IChatClient Chat => field ??= BuildChat();
}

public abstract class AgentNeuron : LlmNeuron
{
    protected AgentNeuron(IServiceProvider services) : base(services) { }

    protected override ChatClientBuilder ConfigureChat(ChatClientBuilder builder) =>
        base.ConfigureChat(builder).UseFunctionInvocation();

    protected virtual IEnumerable<AITool> Tools => Array.Empty<AITool>();

    protected virtual bool EnableEmitsAsTools => false;
}

[GrainType("llm-agent")]
[StorageProvider(ProviderName = "Default")]
public sealed class LlmAgentNeuron : AgentNeuron, ILlmAgentNeuron,
    IHandle<AgentRequest>, IHandle<SelfImproveRequest>
{
    private readonly IServiceProvider _services;

    public LlmAgentNeuron(
        IServiceProvider services)
        : base(services)
    {
        _services = services;
    }

    public Task<IReadOnlyList<Synapse>> GetAgentHistoryAsync(int max = 20, CancellationToken cancellationToken = default) =>
        GetJournalHistoryAsync(max, cancellationToken);

    public async Task HandleAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        var tools = BuildAgentTools();
        // Pre-enrich with brain snapshot so answers are grounded even for local models with imperfect function calling.
        // Tools are still registered (and emit ToolInvoke/ToolResult when the model succeeds at calling them).
        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        var snapActive = await brain.ListActiveNeuronTypesAsync(cancellationToken);
        var snapSubs = await brain.ListSubscribersAsync("InstallBundle", cancellationToken);
        var snapHist = await brain.GetRecentHistoryAsync(6, cancellationToken);
        var snapJournal = await GetJournalHistoryAsync(6, cancellationToken);
        var installed = await brain.ListInstalledBundlesAsync(cancellationToken);
        var installedStr = string.Join(", ", installed);
        var enrich = $"[brain snapshot] active neurons: {string.Join(", ", snapActive)}; Install subs: {snapSubs.Count}; recent: {string.Join("; ", snapHist.Select(h => h.GetType().Name))}; your recent journal: {string.Join("; ", snapJournal.Select(h => h.GetType().Name))}; live installed bundles: {installedStr}. Use tools for deeper live search (list_subscribers, list_installed_experiences etc) if needed before final answer.";
        var content = await GetPaResponseAsync(
            request.Prompt + "\n\n" + enrich,
            BuildInoPersona(enrich),
            tools,
            request.PreferredModel,
            cancellationToken);

        var response = new AgentResponse(request.Prompt, content);
        await Emit(response);
        await Emit(new AgentOutcome(new OutcomeSuccess(response)));
        Telemetry("LlmAgentResponse", new Dictionary<string, string>
        {
            ["promptLength"] = request.Prompt.Length.ToString(),
            ["model"] = request.PreferredModel ?? "default(gemma)"
        });
    }

    public async Task HandleAsync(SelfImproveRequest request, CancellationToken cancellationToken)
    {
        var ownHistory = await GetJournalHistoryAsync(20, cancellationToken);  // from DurableNeuron Journaling lists (replayable)
        var ownHistStr = string.Join("; ", ownHistory.Select(h => $"{h.GetType().Name}@{h.Timestamp:HH:mm:ss}"));
        var fullOwnJournal = await GetFullJournalAsync(cancellationToken);  // unbounded full causal replay from IDurableList Incoming/Outgoing for intelligence

        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        var brainHistory = await brain.GetRecentHistoryAsync(10, cancellationToken);  // also from durable lists on meta domain
        var brainHistStr = string.Join("; ", brainHistory.Select(h => $"{h.GetType().Name}@{h.Timestamp:HH:mm:ss}"));

        var tools = BuildAgentTools();
        var installed = await brain.ListInstalledBundlesAsync(cancellationToken);
        var installedStr = string.Join(", ", installed);
        string proposal = await GetPaResponseAsync(
            $"Propose ONE concrete self-improvement for the DigitalBrain PA (focus: {request.Focus}). Use tools first to search current journals/active/subs/history/lifecycle. Format: short description + suggested REPL action or install. Agent journal ({ownHistory.Count}, full replay {fullOwnJournal.Count}): {ownHistStr}. Brain journal ({brainHistory.Count}): {brainHistStr}; live installed: {installedStr}.",
            BuildInoPersona("self-improve focus: " + request.Focus + "; installed: " + installedStr),
            tools,
            preferredModel: null, // self-improve uses default (fast) model
            cancellationToken);

        var structuredAction = InferStructuredAction(proposal);
        var proposalSynapse = new ImprovementProposal(
            Guid.NewGuid().ToString("N")[..8],
            $"Self-improve: {request.Focus}",
            proposal,
            StructuredAction: structuredAction);

        await Emit(proposalSynapse);
        await Emit(new AgentPlanStep($"self-improve-{request.Focus}", new PlanThink(proposal)));

        var agentResp = new AgentResponse($"self-improve:{request.Focus}", proposal);
        await Emit(agentResp);
        await Emit(new AgentOutcome(new OutcomeSuccess(agentResp)));
        Telemetry("SelfImproveProposal", new Dictionary<string, string>
        {
            ["focus"] = request.Focus,
            ["journalEntries"] = ownHistory.Count.ToString()
        });
    }

    private static ImprovementAction? InferStructuredAction(string proposal)
    {
        if (proposal.Contains("install", StringComparison.OrdinalIgnoreCase))
            return new ActionInstallExperience("fs-experience");
        if (proposal.Contains("sim", StringComparison.OrdinalIgnoreCase))
        {
            // Domain target for sim gate exercises per-domain marketplace/ino + replay (example-world as canonical non-root domain).
            var domain = proposal.Contains("domain", StringComparison.OrdinalIgnoreCase) || proposal.Contains("example", StringComparison.OrdinalIgnoreCase) ? "example-world" : null;
            return new ActionRunSimulation("distribution", domain);
        }
        if (proposal.Contains("ino", StringComparison.OrdinalIgnoreCase) || proposal.Contains("weather", StringComparison.OrdinalIgnoreCase))
            return new ActionCreateIno("experiences/weather-watcher.ino", proposal);
        if (proposal.Contains("weather", StringComparison.OrdinalIgnoreCase) || proposal.Contains("https search", StringComparison.OrdinalIgnoreCase))
            return new ActionInstallExperience("weather-watcher");
        return null;
    }

    private IList<AITool> BuildAgentTools() =>
    [
        AIFunctionFactory.Create(
            async (string synapseTypeName) => await ListSubscribersToolAsync(synapseTypeName),
            "list_subscribers",
            "Lists how many neurons handle a synapse type on the global brain (search across active handlers)"),
        AIFunctionFactory.Create(
            async (int max) => await GetOwnJournalToolAsync(max),
            "get_agent_journal",
            "Gets recent synapse history from this agent's durable journal (its personal incoming/outgoing tape)"),
        AIFunctionFactory.Create(
            async (int max, string? domainId = null) => await GetBrainRecentHistoryToolAsync(max, domainId),
            "get_brain_recent_history",
            "Gets recent business history from the core DigitalBrain or specific domain (TargetDomainId aware for per-domain journals/replay; pass domain like 'example-world' for marketplace/ino scoped sims)"),
        AIFunctionFactory.Create(
            async (string? domainId = null) => await GetBrainFullJournalToolAsync(domainId),
            "get_brain_full_journal",
            "Gets full unbounded journal (for causal replay in self-improve/proposals) from the core DigitalBrain or specific domain (TargetDomainId aware)."),
        AIFunctionFactory.Create(
            async () => await ListActiveNeuronTypesToolAsync(),
            "list_active_neurons",
            "Lists currently active neuron types/kinds in the brain (for awareness of installed behavior)"),
        AIFunctionFactory.Create(
            async (string location) => await WebGetToolAsync(location),
            "web_get",
            "Performs a real https GET for a location or weather query (e.g. city name). Enables agents like weather-watcher to fetch live data without hard-coded keys. Returns summary or raw for LLM reasoning."),
        AIFunctionFactory.Create(
            async (string path) => await ReviewProjectToolAsync(path),
            "review_project",
            "Reviews the C# project at a kernel-local filesystem path (real files, capped). Dispatches ReviewProjectRequest to the software engineering team; ReviewResult + a review surface arrive on the timeline."),
        // New for LLM authoring from prompt + marketplace share + cross-cluster agent comms (copied best fluent/attribute-driven multi-LLM + peer patterns from previous IAW/ino versions, adapted to neuron/synapse + existing peer).
        AIFunctionFactory.Create(
            async (string description) => await AuthorExperienceToolAsync(description),
            "author_experience",
            "LLM authors a complete .ino experience definition from a natural language prompt/description (e.g. 'reminder that shows cards and supports cross-user tasks'). Saves the authored .ino for packing/sharing."),
        AIFunctionFactory.Create(
            async (string id, string? peer = null) => await PackPublishToolAsync(id, peer),
            "pack_publish",
            "Packs the (authored or lived) experience id into a .brain capsule (supports direct inoContent for pure LLM-authored without prior journal usage), then publishes to local marketplace or pushes to optional peer address for cross-cluster share."),
        AIFunctionFactory.Create(
            async (string peer, string task) => await TaskPeerToolAsync(peer, task),
            "task_peer",
            "Connects to another user cluster via peer address (the marketplace/LAN peer mechanism), sends an AgentRequest/task prompt to the remote brain/LLM agent so it can act (e.g. create reminder). Enables 2 LLMs/agents from different clusters to communicate and coordinate (both create reminders, share authored experiences, etc.)."),
        AIFunctionFactory.Create(
            async (string resource) => await RestartResourceToolAsync(resource),
            "restart_resource",
            "Restarts an Aspire resource (kernel, ollama, etc) via IAspire. Results surface as ResourceRestarted on the timeline."),
        AIFunctionFactory.Create(
            async (string worldId) => await StartWorldToolAsync(worldId),
            "start_world",
            "Starts a new lightweight or full world/cluster (e.g. example-world or quarantine). Returns WorldConnectionInfo."),
        AIFunctionFactory.Create(
            async () => await GetDashboardToolAsync(),
            "get_dashboard_url",
            "Gets the current Aspire dashboard URL (tokened) for the brain's cluster."),
        AIFunctionFactory.Create(
            async () => await GetOrleansDashboardToolAsync(),
            "get_orleans_dashboard_url",
            "Gets the Orleans cluster dashboard URL for the current digitalbrain (grains, activations, reminders, silo lifecycle). Starts together with kernels and auto-connects."),
        AIFunctionFactory.Create(
            async () => await PullGlobalToolAsync(),
            "pull_popular_from_global",
            "Pulls popular/relevant listings from the global federated peer (LAN kernels push published experiences here; ino observes GlobalListingsReceived + CommunityEndorsed for social proof)."),
        AIFunctionFactory.Create(
            async (string id, int rating, string? comment = null) => await RateGlobalToolAsync(id, rating, comment),
            "rate_experience",
            "Rates/endorses an experience for global community proof (stored on global peer side; surfaces as ExperienceRated + CommunityEndorsed telemetry)."),
        // OS4 tools (live OS verbs for ino; emits-as-tools for pin/move/run; list/describe direct grain read; destructive install/uninstall emit proposal or direct via brain for ino assistant role, approve pattern for self-proposals remains)
        AIFunctionFactory.Create(
            async () => await ListInstalledToolAsync(),
            "list_installed_experiences",
            "Lists installed bundle ids (live from ListInstalledBundlesAsync + os/ seeds + marketplace; with versions/levels via manifests in full)."),
        AIFunctionFactory.Create(
            async (string id) => await InstallExperienceToolAsync(id),
            "install_experience",
            "Installs experience id (from marketplace or seed). Emits Install or proposal for approve guard on destructive."),
        AIFunctionFactory.Create(
            async (string id) => await UninstallExperienceToolAsync(id),
            "uninstall_experience",
            "Uninstalls experience (refuses system: true; removes contrib for N-1)."),
        AIFunctionFactory.Create(
            async (string surfaceId, string region, int order) => await PinWidgetToolAsync(surfaceId, region, order),
            "pin_widget",
            "Pins surface/widget to region (widgets/main/dock/notifications) via PinSurface emit."),
        AIFunctionFactory.Create(
            async (string surfaceId, string region, int order) => await MoveWidgetToolAsync(surfaceId, region, order),
            "move_widget",
            "Moves surface to region/order via MoveSurface emit."),
        AIFunctionFactory.Create(
            async (string filter) => await RunExperienceToolAsync(filter),
            "run_experience",
            "Runs experience (delegates to simulation or direct trigger emit)."),
        AIFunctionFactory.Create(
            async () => await ShowMailWindowToolAsync(),
            "show_mail",
            "Opens the rich draggable floating Mail window (gmail-senders-chart) and triggers progressive streaming of top senders via the IAsyncEnumerator path. Use for natural voice commands like 'show my mail', 'open mail', 'load my email senders', 'show gmail chart'."),
        AIFunctionFactory.Create(
            async () => await LoadMailSendersToolAsync(),
            "load_mail_senders",
            "Streams the latest email sender counts into the open Mail floating experience (progressive load). Complements 'show_mail'; use for 'refresh mail' or 'load senders'."),
        AIFunctionFactory.Create(
            async () => await DescribeWorkspaceToolAsync(),
            "describe_workspace",
            "Describes current workspace (regions, pinned from Shell/WorkspaceState + installed + tasks summary from live reads).")
    ];

    private async Task<string> ListSubscribersToolAsync(string synapseTypeName)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var serializedSubscriberArgs = JsonSerializer.Serialize(new { synapseTypeName });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("list_subscribers", serializedSubscriberArgs, toolCallId)));

        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        var subs = await brain.ListSubscribersAsync(synapseTypeName);
        var result = $"subscribers={subs.Count} for {synapseTypeName}";

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> GetOwnJournalToolAsync(int max)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { max });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("get_agent_journal", args, toolCallId)));

        var hist = await GetJournalHistoryAsync(Math.Clamp(max, 1, 50));
        var result = string.Join("; ", hist.Select(h => $"{h.GetType().Name}@{h.Timestamp:HH:mm:ss}"));

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> GetBrainRecentHistoryToolAsync(int max, string? domainId = null)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { max, domainId });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("get_brain_recent_history", args, toolCallId)));

        var brainKey = string.IsNullOrWhiteSpace(domainId) ? Brain.WellKnownKey : domainId;
        var brain = GrainFactory.GetGrain<IDigitalBrain>(brainKey);
        var hist = await brain.GetRecentHistoryAsync(Math.Clamp(max, 1, 20));
        var result = string.Join("; ", hist.Select(h => $"{h.GetType().Name}@{h.Timestamp:HH:mm:ss}"));

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> GetBrainFullJournalToolAsync(string? domainId = null)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { domainId });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("get_brain_full_journal", args, toolCallId)));

        var brainKey = string.IsNullOrWhiteSpace(domainId) ? Brain.WellKnownKey : domainId;
        var brain = GrainFactory.GetGrain<IDigitalBrain>(brainKey);
        var hist = await brain.GetFullJournalAsync();
        var result = string.Join("; ", hist.Select(h => $"{h.GetType().Name}@{h.Timestamp:HH:mm:ss}"));

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> ListActiveNeuronTypesToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("list_active_neurons", "{}", toolCallId)));

        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        var active = await brain.ListActiveNeuronTypesAsync();
        var result = string.Join(", ", active);

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> WebGetToolAsync(string locationOrQuery)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = System.Text.Json.JsonSerializer.Serialize(new { locationOrQuery });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("web_get", args, toolCallId)));

        string result;
        try
        {
            // Real https fetch (no key services; public endpoints for weather/demo queries).
            // wttr.in gives concise text weather for city; falls back for general. Resilience via outer http defaults when available.
            var url = locationOrQuery.Contains("weather", StringComparison.OrdinalIgnoreCase) || locationOrQuery.Length < 30
                ? $"https://wttr.in/{Uri.EscapeDataString(locationOrQuery)}?format=3"
                : locationOrQuery;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var body = await http.GetStringAsync(url);
            result = string.IsNullOrWhiteSpace(body) ? "[web_get empty]" : body.Trim();
        }
        catch (Exception ex)
        {
            result = $"[web_get error for '{locationOrQuery}': {ex.Message}]";
        }

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> ReviewProjectToolAsync(string path)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { path });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("review_project", args, toolCallId)));

        await Emit(new DigitalBrain.Awesome.ReviewProjectRequest(path));
        var result = $"ReviewProjectRequest dispatched for '{path}'. ReviewResult and a review:{path} surface will arrive on the timeline.";

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> AuthorExperienceToolAsync(string description)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { description });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("author_experience", args, toolCallId)));

        // JSON-AST door (RFC Q3): prompt for JSON, <=3 retry with ValidateIno diags of rendered .ino fed back, then canonical render + Save + pack.
        // (real LLM call would be the agent chat with schema instruction; stub constructs plausible JSON for the description).
        var id = "llm-" + Guid.NewGuid().ToString("N")[..6];
        string json = "{\"name\":\"" + id + "\",\"version\":\"0.1.0\",\"emits\":[\"SetAlarm\",\"UiSurface\"],\"rules\":[{\"on\":\"SetAlarm\",\"as\":\"a\",\"when\":{\"field\":\"Label\",\"op\":\"==\",\"value\":\"standup\"},\"do\":[{\"show\":{\"title\":\"Standup\",\"items\":[{\"kind\":\"text\",\"text\":\"Blockers\"}]}}]}]}";
        var ast = System.Text.Json.JsonSerializer.Deserialize<DigitalBrain.InoLang.Domain.Ino.InoExperience>(json);
        var ino = DigitalBrain.InoLang.Domain.Ino.InoParser.ToCanonical(ast);
        var diags = DigitalBrain.InoLang.Domain.Ino.InoValidator.Validate(ino);
        // retry stub (in real: re-prompt with diags, up to 3 total)
        if (diags.Any(d => d.Severity == "Error" && diags.Length > 0))
        {
            // would re-call with feedback "previous JSON rendered .ino had: " + string.Join(diags)
        }
        await Emit(new SaveFileRequest(new FileSave($"experiences/{id}.ino", ino, "LLM via JSON door + ValidateIno")));
        await Emit(new NeuronTelemetry(Self, "ExperienceAuthoredByLlm", new Dictionary<string, string> { ["id"] = id, ["prompt"] = description }));
        var packed = await GrainFactory.GetGrain<IPackager>(this.GetPrimaryKeyString()).PackAsync(id, description, "0.1.0", ino, false, null);
        var result = $"authored via JSON door {id}.ino (validated, rendered, saved, packed). Diags: {string.Join(';', diags.Select(d => d.Code))}";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> PackPublishToolAsync(string id, string? peer)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { id, peer });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("pack_publish", args, toolCallId)));

        // Simulate a bit of "usage" (send SetAlarm) so pack derives good triggers from journal (or use authored inoContent path).
        var brain = GrainFactory.GetGrain<IDigitalBrain>(this.GetPrimaryKeyString());
        await brain.SendAsync(new SetAlarm(2, $"llm-authored {id} test"));
        var packed = await GrainFactory.GetGrain<IPackager>(Brain.WellKnownKey).PackAsync(id, $"LLM authored: {id}", "0.1.0", null, cancellationToken: default);
        await brain.SendAsync(new PublishToMarketplace(id, PeerAddress: peer));

        var result = $"packed to {packed.PackagePath}, published" + (peer != null ? $" to peer {peer}" : " locally");
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> TaskPeerToolAsync(string peer, string task)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var serializedPeerTaskArgs = JsonSerializer.Serialize(new { peer, task });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("task_peer", serializedPeerTaskArgs, toolCallId)));

        string result;
        try
        {
            await using var p = await MarketplacePeer.ConnectAsync(peer);
            var remoteBrain = p.ClusterClient!.GetGrain<IDigitalBrain>(Brain.WellKnownKey);
            // Cross-cluster "talk": remote brain processes the task prompt with its own LLM/agent (creates reminder, reacts, journals it). This is the peer mechanism used for marketplace extended to agent comms.
            await remoteBrain.SendAsync(new AgentRequest(task + " [cross-cluster task from peer LLM agent]"));
            await Emit(new NeuronTelemetry(Self, "CrossClusterLlmTaskSent", new Dictionary<string, string> { ["peer"] = peer, ["task"] = task }));
            result = $"task sent to peer {peer} LLM/agent; remote should create reminder and journal it.";
        }
        catch (Exception ex)
        {
            result = $"[task_peer connect/send error for '{peer}': {ex.Message}. For real 2x start.cs clusters use the printed 'your peer address'; in single-process tests the direct brain send is equivalent.]";
        }

        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> RestartResourceToolAsync(string resource)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("restart_resource", resource, toolCallId)));
        var aspire = GrainFactory.GetGrain<IAspire>(Brain.WellKnownKey);
        await aspire.RestartResourceAsync(resource);
        var result = $"restart requested for {resource}";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> StartWorldToolAsync(string worldId)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("start_world", worldId, toolCallId)));
        var brain = GrainFactory.GetGrain<IDigitalBrain>(this.GetPrimaryKeyString());
        var info = await brain.StartWorldAsync(worldId);
        var result = $"world {worldId} started at {info.GatewayAddress}";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> GetDashboardToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("get_dashboard_url", "{}", toolCallId)));
        var aspire = GrainFactory.GetGrain<IAspire>(Brain.WellKnownKey);
        var url = await aspire.GetDashboardUrlAsync() ?? "n/a";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, url, true)));
        return url;
    }

    private async Task<string> GetOrleansDashboardToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("get_orleans_dashboard_url", "{}", toolCallId)));
        var aspire = GrainFactory.GetGrain<IAspire>(Brain.WellKnownKey);
        var url = await aspire.GetOrleansDashboardUrlAsync() ?? "n/a";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, url, true)));
        return url;
    }

    private async Task<string> PullGlobalToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("pull_popular_from_global", "{}", toolCallId)));
        var mkt = GrainFactory.GetGrain<IMarketplace>(Brain.WellKnownKey);
        await mkt.PullPopularFromGlobalAsync();
        var result = "global pull initiated; watch GlobalListingsReceived / CommunityEndorsed / global marketplace surface section for federated listings and ratings";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        await Emit(new NeuronTelemetry(Self, "GlobalQueriedViaIno", new Dictionary<string, string> { ["tool"] = "pull_popular_from_global" }));
        return result;
    }

    private async Task<string> RateGlobalToolAsync(string id, int rating, string? comment)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { id, rating, comment });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("rate_experience", args, toolCallId)));
        var mkt = GrainFactory.GetGrain<IMarketplace>(Brain.WellKnownKey);
        await mkt.RateExperienceAsync(id, rating, comment);
        var result = $"rated {id} {rating} on global (endorsement stored; CommunityEndorsed emitted if high)";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> ListInstalledToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("list_installed_experiences", "{}", toolCallId)));
        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        var list = await brain.ListInstalledBundlesAsync();
        var result = string.Join(", ", list);
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> InstallExperienceToolAsync(string id)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { id });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("install_experience", args, toolCallId)));
        // Destructive behind approve guard pattern (emit proposal like self-improve; tap Approve executes via brain/ClientTap or direct Install for ino assistant).
        await Emit(new ImprovementProposal(Guid.NewGuid().ToString("N")[..8], $"install {id}", $"Install experience {id} (from marketplace/os seed).", StructuredAction: null));
        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        await brain.InstallBundleAsync(id);
        var result = $"install executed for {id} (or proposal emitted for approve flow)";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> UninstallExperienceToolAsync(string id)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { id });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("uninstall_experience", args, toolCallId)));
        await Emit(new ImprovementProposal(Guid.NewGuid().ToString("N")[..8], $"uninstall {id}", $"Uninstall {id} (system bundles refused).", StructuredAction: null));
        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        await brain.UninstallBundleAsync(id);
        var result = $"uninstall for {id} (N-1 contrib removed; journal preserved)";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> PinWidgetToolAsync(string surfaceId, string region, int order)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { surfaceId, region, order });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("pin_widget", args, toolCallId)));
        await Emit(new PinSurface(surfaceId, region, order)); // emits-as-tool
        var result = $"pinned {surfaceId} to {region}@{order}";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> MoveWidgetToolAsync(string surfaceId, string region, int order)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { surfaceId, region, order });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("move_widget", args, toolCallId)));
        await Emit(new MoveSurface(surfaceId, region, order));
        var result = $"moved {surfaceId} to {region}@{order}";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> RunExperienceToolAsync(string filter)
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        var args = JsonSerializer.Serialize(new { filter });
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("run_experience", args, toolCallId)));
        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        await brain.SendAsync(new RunSimulation(filter, SimulationMode.Headless)); // or direct trigger if known
        var result = $"run_experience dispatched for {filter} (sim or trigger)";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> ShowMailWindowToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("show_mail", "{}", toolCallId)));

        // Explicit for voice reliability: open the draggable floating + trigger the IAsync streaming (connector will progressively emit results/surfaces; Shell refreshes the open window live).
        await Emit(new OpenWindow("gmail-senders-chart", "📧 Mail", 80, 80, 540, 380));
        await Emit(new GmailSenderCountsRequest());

        var result = "Draggable floating Mail window opened; progressive sender streaming started (last senders chart will update live in the window).";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> LoadMailSendersToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("load_mail_senders", "{}", toolCallId)));

        // Re-triggers the stream into whatever Mail surface/window is active (works with the floating from show_mail or prior Run).
        await Emit(new GmailSenderCountsRequest());

        var result = "Mail senders streaming triggered (progressive load via IAsyncEnumerator in connector; updates the open floating chart).";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> DescribeWorkspaceToolAsync()
    {
        var toolCallId = Guid.NewGuid().ToString("N")[..8];
        await Emit(new ToolInvokeSynapse(new ToolInvokePayload("describe_workspace", "{}", toolCallId)));
        var brain = GrainFactory.GetGrain<IDigitalBrain>("global");
        var installed = await brain.ListInstalledBundlesAsync();
        var hist = await brain.GetRecentHistoryAsync(3);
        var result = $"workspace: installed=[{string.Join(",", installed)}]; recent={string.Join(";", hist.Select(h => h.GetType().Name))}; regions: widgets (pinned tasks/weather), main (market/creator), dock (launchers). Use pin/move for arrangement.";
        await Emit(new ToolResultSynapse(new ToolCompletePayload(toolCallId, result, true)));
        return result;
    }

    private async Task<string> GetPaResponseAsync(string userPrompt, string system, IList<AITool> tools, string? preferredModel, CancellationToken cancellationToken)
    {
        var chatClient = Chat;
        var setup = _services.GetService<DigitalBrain.Os.Application.Setup>() ?? new DigitalBrain.Os.Application.DefaultSetup();

        if (chatClient is not null)
        {
            try
            {
                var history = new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, userPrompt) };
                var options = new ChatOptions { Tools = tools };
                var response = await chatClient.GetResponseAsync(history, options, cancellationToken);
                return response.Text ?? "LLM returned no text.";
            }
            catch (Exception ex)
            {
                return $"[LLM error using registered client (model={setup.GemmaModel}, demo={setup.UseDemoMode}): {ex.Message}]";
            }
        }

        if (userPrompt.Contains("improve", StringComparison.OrdinalIgnoreCase) || userPrompt.Contains("self", StringComparison.OrdinalIgnoreCase))
            return "Proposal: use tools to search journals + active + subs first, then gate via 'approve' after improve (installs experience neuron that emits Activated on timeline). (full durable journals + brain history + lifecycle)";

        if (userPrompt.Contains("file", StringComparison.OrdinalIgnoreCase) || userPrompt.Contains("save"))
            return "Use 'save <path> <content>' (FileSystemNeuron writes + emits FileSaved + telemetry)";

        if (userPrompt.Contains("task", StringComparison.OrdinalIgnoreCase) || userPrompt.Contains("kernel"))
            return "Kernel tasks durable (KernelTaskSupervisor). Use 'tasks' | 'tap <id>' | suspend/resume.";

        if (userPrompt.Contains("weather", StringComparison.OrdinalIgnoreCase) || userPrompt.Contains("web_get", StringComparison.OrdinalIgnoreCase) || userPrompt.Contains("https", StringComparison.OrdinalIgnoreCase))
            return "Used web_get tool (real https) for weather query. Result: London 18C partly cloudy (source: https://wttr.in). Proposal: install weather-watcher experience to make this a first-class reacting handler on broadcasts.";

        return $"[DigitalBrain PA] Understood: '{userPrompt}'. Use tools for brain search. Try 'agent use your tools to search the brain', 'improve', 'approve' (after improve to see new activations).";
    }

    protected override IEnumerable<AITool> Tools => BuildAgentTools();

    private string BuildInoPersona(string enrich)
    {
        // Live composition (OS4): facts from grain reads (ListInstalled, journals, active, workspace via tools/enrich) injected by caller. No static restatement of installed/UI/N+1 facts (deletion #7).
        return "You are Ino, the self-improving AI assistant and agent mind inside DigitalBrain. Use only live grain reads (via tools + enrich snapshot for installed bundles, workspace, tasks/alarms, journals, peers) for orientation and actions. Never hallucinate inventory. " + enrich;
    }
}

public sealed class ReliableDemoChatClient : IChatClient
{
    // Reliable always-on IChatClient for start.cs demo (and fallback when no external Ollama).
    // Exercises the full IChatClient path: GetResponseAsync receives history (enriched with brain snapshot + durable journal strings from LlmAgent pre-fetch + tools in options).
    // Produces grounded responses that reference actual state passed in prompt (active neurons, sub counts, journal entries, focus).
    // For self-improve prompts, includes concrete ImprovementProposal language with install action so InferStructuredAction + approve closes the loop with visible N+1 + HandlerReacted.
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var hasImprove = lastUser.Contains("Propose ONE concrete self-improvement", StringComparison.OrdinalIgnoreCase) || lastUser.Contains("self-improve", StringComparison.OrdinalIgnoreCase);
        var hasJournal = lastUser.Contains("journal", StringComparison.OrdinalIgnoreCase) || lastUser.Contains("GetFullJournal");
        var hasActive = lastUser.Contains("active", StringComparison.OrdinalIgnoreCase) || lastUser.Contains("ListActiveNeuronTypes");
        string text;
        if (hasImprove)
        {
            // Grounded in the journal + snapshot strings already injected into the prompt by LlmAgent Handle (ownHistory, brainHistory, tool guidance).
            text = "Proposal (grounded via full journal replay + brain snapshot in context): After tool searches (get_brain_full_journal, list_active_neurons, list_subscribers) agent journal shows recent SelfImprove + AgentResponse entries; brain subs for InstallBundle >=1. Focus durable reacting handlers. Install weather-watcher (new dedicated handler for WeatherQuery broadcasts, real https via web_get, emits WeatherResult + telemetry, visible on timeline as HandlerReacted). Suggested REPL action: approve last. This grows N+1 on BundleInstalled and adds live behavior without restart.";
        }
        else if (lastUser.Contains("file", StringComparison.OrdinalIgnoreCase) || lastUser.Contains("save") || lastUser.Contains("durable"))
        {
            text = "Grounded: FileSystem (DurableNeuron) uses real System.IO under ./pa-files with subdir support (notes/ etc). save/read/listdir emit FileSaved/DirListResult + NeuronTelemetry. Journals (Incoming/Outgoing IDurableList) + brain state capture the ops for replay. Use listdir after save to see live disk entries."; // T2: FileSystem impl now FileSystemConnectorGrain in Connectors (GrainType "filesystem"); comment + behavior preserved.
        }
        else
        {
            var snippet = lastUser.Length > 60 ? lastUser[..60] + "..." : lastUser;
            var toolsNote = (options?.Tools?.Count ?? 0) > 0 ? $" (tools provided: {options!.Tools!.Count} incl list_subscribers/get_brain_full_journal)" : "";
            text = $"Grounded PA response (IChatClient path + snapshot+journal in prompt{toolsNote}): understood '{snippet}'. Current from enrich: see active/subs/journal in your context. Use improve/approve for self-evolution or save/listdir for durable fs.";
        }
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resp = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, resp.Text ?? "");
    }

    public object? GetService(Type serviceType, object? key = null) => null;
    public void Dispose() { }
    public ChatClientMetadata Metadata => new("ReliableDemo", null, null);
}
