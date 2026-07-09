using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Models;
using DigitalBrain.Ino.Context;
using DigitalBrain.Kernel;
using DigitalBrain.Ui.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Ino;

using DigitalBrain.Ui.Contracts;

[GrainType("ino.personal.v1")]
public partial class InoNeuron(ILogger<InoNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IInoNeuron, IHandle<Signal>
{
    private sealed record AutomationDraft(string When, string? Target, string Script, string Rationale);

    private const string LlmUnavailableReply =
        "The local LLM is not ready yet. Ollama may still be pulling or loading the model; try again in a moment.";

    private const string ToolCallHallucinationFallback =
        "I tried to use a tool for that but didn't get a clean result. Please try again, or rephrase your request.";

    [GeneratedRegex(@"""name""\s*:\s*""[A-Za-z_][A-Za-z0-9_]*""\s*,\s*""arguments""\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex ToolCallShapeRegex();

    private static bool LooksLikeUnexecutedToolCall(string text) => ToolCallShapeRegex().IsMatch(text);

    private static readonly string[] AllowedAutomationTriggers =
    [
        "NeuronActivated"
    ];

    private static readonly IReadOnlyDictionary<string, string> LlmProviderCommands =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ollama"] = "ollama",
            ["azureopenai"] = "azureopenai"
        };

    private static readonly Regex LlmProviderCommandRegex =
        new(@"^\s*set-llm:(?<provider>[A-Za-z0-9._-]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const int RecentOutgoingForContext = 8;
    private const int RecentIncomingForContext = 5;
    private const int RecentCompletedTasksForContext = 3;
    private const int RecentMemoriesForContext = 5;
    private const int RecentAutomationsForContext = 3;
    private const int RecentCombinedForMemorySummary = 20;
    private const int MinJournalsForMemorySummary = 5;
    private const int RecentConversationTurnsForContext = 12;
    private const string AnonymousClientId = "anonymous";
    private const string ConversationTurnUserRole = "user";
    private const string ConversationTurnAssistantRole = "assistant";

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await RegisterDiscoveredAgentCapabilitiesAsync(ct);
        await RememberCapabilitiesAsync(ct);
    }

    private async Task RegisterDiscoveredAgentCapabilitiesAsync(CancellationToken cancellationToken)
    {
        foreach (var record in InoAgentCapabilities.DiscoverAgentRecords())
        {
            if (!HasCapabilityRegistration(record.Id, record.Origin))
            {
                await FireAsync(record.ToCapabilityRegistered(), cancellationToken);
            }
        }
    }

    private bool HasCapabilityRegistration(string id, string origin) =>
        OutgoingJournal.Concat(IncomingJournal)
            .OfType<CapabilityRegistered>()
            .Any(reg => string.Equals(reg.Id, id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(reg.Origin, origin, StringComparison.OrdinalIgnoreCase));

    private async Task RememberCapabilitiesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var context = GrainFactory.GetGrain<IContextNeuron>(IContextNeuron.SingletonKey);
            var records = await LoadCapabilityRecordsAsync(cancellationToken);
            foreach (var record in records)
            {
                await context.RememberEvidenceAsync(
                    record.ToMemoryText(),
                    WorkspaceIds.Default,
                    record.SourceKind,
                    record.TrustLevel,
                    record.Origin,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch { /* context optional for classifier grounding */ }
    }

    public async Task HandleAsync(InoRequest request, CancellationToken cancellationToken = default)
    {
        var workspaceId = WorkspaceIds.Effective(request.WorkspaceId);
        cancellationToken.ThrowIfCancellationRequested();
        var capabilities = await LoadCapabilityRecordsAsync(cancellationToken);

        if (await TryHandleCapabilityQuestionAsync(request, workspaceId, capabilities, cancellationToken))
        {
            return;
        }

        if (await TryHandleExplanationQuestionAsync(request, workspaceId, cancellationToken))
        {
            return;
        }

        var classification = await InoIntentClassifier.ClassifyWithLlmAsync(request.Prompt, ServiceProvider, capabilities, cancellationToken);
        if (classification.Intent == "uikit_gallery")
        {
            await DeliverUiKitGallerySurfaceAsync(request.ClientId, workspaceId, cancellationToken);
            await FireAsync(new InoResponse(request.Prompt, "UiKit component gallery:", []), cancellationToken);
            return;
        }

        if (classification.Intent == "set_llm")
        {
            await HandleLlmSetCommandAsync(request, workspaceId, cancellationToken);
            return;
        }

        if (classification.Intent == "llm_settings")
        {
            await DeliverLlmSettingsSurfaceAsync(request.ClientId, workspaceId, cancellationToken);
            await FireAsync(new InoResponse(request.Prompt, "LLM / model settings:", []), cancellationToken);
            return;
        }

        if (classification.Intent == "automation_create")
        {
            await HandleAutomationCreateIntentAsync(request, workspaceId, capabilities, cancellationToken);
            return;
        }

        if (classification.Intent == "approve")
        {
            await HandleApproveProposalIntentAsync(request, workspaceId, cancellationToken);
            return;
        }

        if (classification.Intent == "run_automation")
        {
            var reply = "Running the requested automation (preview or activated). Check the Tasks surface for results.";
            await FireAsync(new InoResponse(request.Prompt, reply, []), cancellationToken);
            await DeliverReplySurfaceAsync(reply, request.ClientId, workspaceId, cancellationToken);
            return;
        }

        // Capability routing for IAgent (gmail, salesforce etc) is now handled via LLM tool calling
        // in the generic path using Microsoft.Agents.AI's ChatClientAgent + AIFunctions.
        // This follows official patterns for tool use, eliminates "Routed to X" dead-ends, and lets the model decide + incorporate results.
        // Custom early dispatch + "routed" reply deleted per 5-steps (trash removal, no duplication of intent logic).

        foreach (var handler in InoIntentHandlers.Default)
        {
            if (await handler.TryHandleAsync(this, request, workspaceId, cancellationToken))
            {
                return;
            }
        }
    }

    private async Task<bool> TryHandleCapabilityQuestionAsync(
        InoRequest request,
        string workspaceId,
        IReadOnlyList<InoCapabilityRecord> capabilities,
        CancellationToken cancellationToken)
    {
        if (!InoCapabilityAnswers.TryCreateAnswer(
                request.Prompt,
                capabilities,
                out var answer))
        {
            return false;
        }

        await FireAsync(new InoResponse(request.Prompt, answer, []), cancellationToken);
        await DeliverReplySurfaceAsync(answer, request.ClientId, workspaceId, cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<InoCapabilityRecord>> LoadCapabilityRecordsAsync(CancellationToken cancellationToken)
    {
        var local = OutgoingJournal.Concat(IncomingJournal).ToArray();
        return await InoCapabilityCatalog.LoadAsync(
            GrainFactory,
            local,
            InoIntentHandlers.CapabilityRecords,
            cancellationToken);
    }

    private async Task<bool> TryHandleExplanationQuestionAsync(InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        if (!InoExplanationFormatter.IsExplanationQuestion(request.Prompt))
        {
            return false;
        }

        var correlationId = InoExplanationFormatter.TryExtractCorrelationId(request.Prompt)
            ?? InoExplanationFormatter.ResolveLastCorrelationId(OutgoingJournal);

        string reply;
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            reply = "I do not have enough lineage yet. No previous action with a correlation id is in my journal.";
        }
        else
        {
            var lineage = await GetCausalLineageAsync(correlationId, cancellationToken);
            reply = InoExplanationFormatter.Format(correlationId, lineage);
        }

        await FireAsync(new InoResponse(request.Prompt, reply, []), cancellationToken);
        await DeliverReplySurfaceAsync(reply, request.ClientId, workspaceId, cancellationToken);
        return true;
    }

    internal async Task HandleRelationGraphIntentAsync(InoRequest request, string workspaceId, CancellationToken cancellationToken = default)
    {
        await FireAsync(new InoResponse(request.Prompt, "Rendered a relation graph.", []), cancellationToken);
        await DeliverGraphSurfaceAsync(
            DbSchemaGraphMapper.RelationOfTwoObjectsTree(),
            request.ClientId,
            workspaceId,
            "Object relation",
            "surface.graph.relation",
            cancellationToken);
    }

    internal async Task<bool> TryHandleSchemaVisualizationIntentAsync(InoRequest request, string workspaceId, CancellationToken cancellationToken = default)
    {
        if (TryExtractDatabasePath(request.Prompt, out var databasePath))
        {
            var inspected = await InspectReferencedDatabaseAsync(databasePath, request.ClientId, workspaceId, cancellationToken);
            if (inspected is not null)
            {
                await FireAsync(new InoResponse(request.Prompt, SchemaReplyText(inspected), []), cancellationToken);
                await FireAsync(inspected, cancellationToken);
                return true;
            }
        }

        var latest = LatestSuccessfulSchema(request.ClientId, workspaceId);
        if (latest?.Schema is not null)
        {
            await FireAsync(new InoResponse(request.Prompt, "Rendered the most recent database schema.", []), cancellationToken);
            await ProcessSchemaInspectedAsync(latest, request.ClientId ?? latest.ClientId, workspaceId, cancellationToken);
            return true;
        }

        return false;
    }

    internal async Task HandleGenericIntentAsync(InoRequest request, string workspaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var ctx = await BuildContextAsync(request.Prompt, workspaceId, cancellationToken);

        var chat = await ResolveGlobalLlmClientAsync(cancellationToken)
            ?? await ResolveToolCapableChatClientAsync(cancellationToken)
            ?? ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            var fallback = LlmUnavailableReply;
            await FireAsync(new InoResponse(request.Prompt, fallback, []), cancellationToken);
            await DeliverReplySurfaceAsync(fallback, request.ClientId, workspaceId, cancellationToken);
            return;
        }

        // Proper Microsoft.Extensions.AI / Agent Framework usage (from Context7 research):
        // - ChatMessage list for conversation history (instead of raw concatenated prompt)
        // - ChatClientAgent.RunAsync for native tool calling (default agent middleware wraps FunctionInvokingChatClient)
        // - Context providers pattern: inject capability catalog + memories + recent journal as messages
        // - Compaction: simple threshold-based summarization of old turns (inspired by SK ChatHistorySummarizationReducer + Agent FW SlidingWindowCompaction)
        // This deletes custom intent classification duplication for agent capabilities; LLM + tools decide and incorporate results directly.
        //
        // The persona line stays a ChatMessage (not ChatClientAgent's `instructions:` parameter) deliberately:
        // Context7 confirms `instructions` travels to the model via a channel separate from `messages` (e.g.
        // ChatOptions.Instructions), which a plain IChatClient is not guaranteed to fold back into the messages
        // it receives - verified empirically against this repo's fake IChatClient test doubles. Keeping it as a
        // message guarantees every IChatClient implementation (real or fake) actually sees it.
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are INO, the personal AI in DigitalBrain (NeuroOS). Use tools for real actions like Gmail access. Always give the useful answer first, then any directives. Incorporate tool results naturally."),
            new(ChatRole.System, "CAPABILITIES AND CONTEXT:\n" + ctx)
        };

        messages.AddRange(LoadConversationHistory(request.ClientId));
        messages.Add(new ChatMessage(ChatRole.User, SecretText.Redact(request.Prompt)));

        // Define tools for capabilities using proper Microsoft.Extensions.AI AIFunction (per Context7 research).
        // LLM decides when to call (e.g. "get my last gmail", "check Salesforce deals").
        // Tools return useful text for LLM + trigger surfaces via grains (real agent access).
        // Scope: for demo/single user the main grain works; full user scope via clientId in production.
        var tools = ServiceProvider.GetServices<IInoToolProvider>()
            .SelectMany(provider => provider.BuildTools(request.ClientId, cancellationToken))
            .ToList();

        var chatOptions = new ChatOptions
        {
            // Spread (not a direct List<AIFunction> assignment) so each element converts to
            // whatever ChatOptions.Tools's element type is, avoiding generic-list invariance issues.
            Tools = [.. tools]
        };

        // Simple compaction before send (delete old if over limit, replace with summary - follows research reducers/compactors).
        // In production keep per-session List<ChatMessage> loaded from journals/state, compact on growth.
        if (messages.Count > 12)
        {
            // Skip messages[0] (persona) and messages[1] ("CAPABILITIES AND CONTEXT") - only summarize and
            // drop actual conversation history turns, never the two fixed system messages.
            var toSummarize = messages.Skip(2).Take(6).ToList();
            var summaryPrompt = "Summarize the following old conversation turns into one concise context paragraph (preserve key facts, no new info): " + string.Join(" | ", toSummarize.Select(m => m.Text ?? ""));
            try
            {
                var sumResp = await chat.GetResponseAsync(summaryPrompt, cancellationToken: cancellationToken);
                var summaryMsg = new ChatMessage(ChatRole.System, "PREVIOUS_CONTEXT_SUMMARY: " + sumResp.Text);
                messages = [messages[0], messages[1], summaryMsg, .. messages.Skip(8)];
            }
            catch { /* keep original */ }
        }

        string finalText;
        try
        {
            AIAgent agent = new ChatClientAgent(chat);
            var response = await agent.RunAsync(messages, session: null, options: new ChatClientAgentRunOptions(chatOptions), cancellationToken: cancellationToken);
            finalText = string.IsNullOrWhiteSpace(response.Text) ? "Done via tools." : response.Text.Trim();
            if (LooksLikeUnexecutedToolCall(finalText))
            {
                Logger.LogWarning("Ino's model emitted an unexecuted tool-call-shaped reply instead of a native tool call: {Reply}", finalText);
                finalText = ToolCallHallucinationFallback;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Logger.LogWarning(ex, "Ino's tool-enabled LLM call failed to reach the model.");
            finalText = LlmUnavailableReply;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Tool-enabled LLM call failed");
            finalText = "I attempted to use tools for your request but hit an issue.";
        }

        var conversationClientId = request.ClientId ?? AnonymousClientId;
        await FireAsync(new InoConversationTurn(conversationClientId, ConversationTurnUserRole, SecretText.Redact(request.Prompt)), cancellationToken);
        await FireAsync(new InoConversationTurn(conversationClientId, ConversationTurnAssistantRole, finalText), cancellationToken);

        // The generic tool-calling path (ChatClientAgent) never produces TASK:/BRANCH: directives itself -
        // that only ever came from the deleted directive-parsing flow - so there is nothing to orchestrate here.
        var taskIds = Array.Empty<string>();

        await FireAsync(new InoResponse(request.Prompt, finalText, taskIds.ToArray()), cancellationToken);
        await DeliverReplySurfaceAsync(finalText, request.ClientId, workspaceId, cancellationToken);

        await CreateMemorySummaryAsync(workspaceId, cancellationToken);
    }
    public async Task HandleAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        // Signals handled via generic catalog + packet path or by owning connector grains.
    }

    private async Task<string> ResolveUserIdAsync(string? clientId, CancellationToken cancellationToken = default)
    {
        var state = await ResolveSessionAsync(clientId, cancellationToken);
        return state?.UserId.Value ?? UserId.Anonymous.Value;
    }

    private async Task<UserSessionState?> ResolveSessionAsync(string? clientId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var session = GrainFactory.GetGrain<IUserSessionNeuron>(IUserSessionNeuron.SingletonKey);
        return await session.GetSessionByClientIdAsync(clientId);
    }

    public async Task HandleAsync(TabularDataIngested ingested, CancellationToken cancellationToken = default)
    {
        var workspaceId = WorkspaceIds.Effective(ingested.WorkspaceId);
        cancellationToken.ThrowIfCancellationRequested();
        var headers = JsonSerializer.Deserialize<List<string>>(ingested.HeadersJson) ?? [];
        var rows = JsonSerializer.Deserialize<List<List<string>>>(ingested.RowsJson) ?? [];

        var tree = new UiWidgetTree(UiKitVocabulary.Panel, new Dictionary<string, object?>(),
        [
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = ingested.FileName }),
            new(UiKitVocabulary.Table, new Dictionary<string, object?> { ["columns"] = headers, ["rows"] = rows }),
        ]);

        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.Title] = "INO",
            ["role"] = "assistant",
        };
        if (ingested.ClientId is not null)
        {
            props["clientId"] = ingested.ClientId;
        }

        props["workspaceId"] = workspaceId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);

        var summary = $"Uploaded '{ingested.FileName}' with columns [{string.Join(", ", headers)}] and {rows.Count} data rows. Column stats: {ingested.ColumnStatsJson}";
        await FireAsync(new MemorySummary(ingested.FileName, summary, DateTimeOffset.UtcNow, workspaceId, "Upload", "UntrustedEvidence", "TabularDataIngested"), cancellationToken);
    }

    public Task HandleAsync(DbSchemaInspected inspected, CancellationToken cancellationToken = default) =>
        ProcessSchemaInspectedAsync(inspected, inspected.ClientId, inspected.WorkspaceId, cancellationToken);

    private async Task ProcessSchemaInspectedAsync(DbSchemaInspected inspected, string? clientId, string? workspaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        if (!inspected.Succeeded || inspected.Schema is null)
        {
            var message = $"I could not inspect database schema '{inspected.ConnectionName}': {inspected.Error ?? "unknown error"}.";
            await DeliverReplySurfaceAsync(message, clientId, workspaceId, cancellationToken);
            return;
        }

        var schema = inspected.Schema with
        {
            SessionId = clientId ?? inspected.Schema.SessionId,
            WorkspaceId = workspaceId
        };
        await DeliverGraphSurfaceAsync(
            DbSchemaGraphMapper.ToGraphCanvasTree(schema),
            clientId ?? schema.SessionId,
            workspaceId,
            $"{schema.ConnectionName} schema",
            "surface.db-schema." + StableSurfaceId(schema.ConnectionName),
            cancellationToken);

        await FireAsync(new MemorySummary(
            schema.ConnectionName,
            SchemaMemorySummary(schema),
            DateTimeOffset.UtcNow,
            workspaceId, "DbSchema", "JournalFact", "DbSupportNeuron"), cancellationToken);
    }

    private async Task<DbSchemaInspected?> InspectReferencedDatabaseAsync(string databasePath, string? clientId, string? workspaceId, CancellationToken cancellationToken = default)
    {
        var connectionName = Path.GetFileNameWithoutExtension(databasePath);
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            connectionName = "sqlite-db";
        }

        workspaceId = WorkspaceIds.Effective(workspaceId);
        var cmd = new DbInspectSchema(connectionName, "sqlite", SourcePath: databasePath, ClientId: clientId, WorkspaceId: workspaceId);
        var db = GrainFactory.GetGrain<IDbSupportNeuron>(IDbSupportNeuron.SingletonKey);
        await db.FireAsync(cmd, cancellationToken);

        var timeline = await db.GetTimelineAsync(cancellationToken);
        return timeline
            .OfType<DbSchemaInspected>()
            .LastOrDefault(result => result.CorrelationId == cmd.SynapseId)
            ?? timeline.OfType<DbSchemaInspected>().LastOrDefault(result => result.ConnectionName == connectionName);
    }

    private async Task DeliverReplySurfaceAsync(string reply, string? clientId, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = reply }),
            [UiSurfaceKeys.Title] = "INO",
            ["role"] = "assistant",
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null)
        {
            props["clientId"] = clientId;
        }

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);
    }

    internal async Task HandleAutomationCreateIntentAsync(
        InoRequest request,
        string workspaceId,
        IReadOnlyList<InoCapabilityRecord> capabilities,
        CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);

        // Structured LLM extraction for high quality automation via catalog-driven signals.
        // Use direct chat for clean JSON output (generic Reason wrapper may add prose).
        string raw;
        var chat = await ResolveGlobalLlmClientAsync(cancellationToken) ?? ServiceProvider.GetService<IChatClient>();
        if (chat is not null)
        {
            var specPrompt =
                "You are a precise automation designer for DigitalBrain. " +
                "Turn the user request into a safe reaction. " +
                "Reply with ONLY minified JSON and nothing else (no code fences, no prose):\n" +
                "{\"when\":\"NeuronActivated\",\"target\":null,\"script\":\"return new[] { new Signal(\\\"TaskCreated\\\", new Dictionary<string,object?>{[\\\"desc\\\"]=\\\"...\\\"}) }; \",\"rationale\":\"short reason\"}\n" +
                "Rules: when must be one of AllowedAutomationTriggers (e.g. NeuronActivated) or from capability signals. " +
                "script: short safe C# returning Signal[]. No file system, loops or unsafe. " +
                "User request: " + request.Prompt;
            var resp = await chat.GetResponseAsync(specPrompt, cancellationToken: cancellationToken);
            raw = resp.Text?.Trim() ?? "";
        }
        else
        {
            var ctx = await BuildContextAsync(request.Prompt, workspaceId, cancellationToken);
            var llmPrompt = "You are helping create a safe DigitalBrain automation. Output ONLY the JSON: {\"when\":\"...\",\"target\":null,\"script\":\"...\",\"rationale\":\"...\"}. User: " + request.Prompt;
            raw = await ReasonWithLlmAsync(llmPrompt, ctx, cancellationToken);
        }

        var defaultWhen = DefaultAutomationTrigger(request.Prompt);
        var defaultScript = "return new[] { new Signal(\"AutomationFired\", new Dictionary<string,object?> { [\"desc\"] = \"from chat\" }) };";
        var defaultRationale = $"Automation proposed from: {request.Prompt}";
        var draft = TryReadAutomationDraft(raw, defaultWhen, defaultScript, defaultRationale, out var parsedDraft)
            ? parsedDraft
            : new AutomationDraft(defaultWhen, null, defaultScript, defaultRationale);

        var autoId = "chat-auto-" + Guid.NewGuid().ToString("N")[..8];
        var proposalId = "automation-" + Guid.NewGuid().ToString("N");
        var scriptId = autoId + "-script";
        var regScript = new RegisterScript(scriptId, draft.Script, "via-ino-chat", Array.Empty<string>(), "default");
        var regReaction = new RegisterReaction(autoId, draft.When, scriptId, draft.Target, Array.Empty<string>(), "default", null);

        var autoGrain = GrainFactory.GetGrain<IAutomationNeuron>("automation-main");
        await autoGrain.FireAsync(new AutomationDefinitionStaged(proposalId, "automation-main", regScript, regReaction), cancellationToken);

        var approval = GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionProposal(
            ProposalId: proposalId,
            Scope: "automation:default",
            Rationale: draft.Rationale,
            ProposedChange: $"Register automation {autoId} (when={draft.When})",
            ApplyVia: SelfEvolutionApplyVia.AutomationDefineReaction,
            Risk: SelfEvolutionRisk.InProcessCode,
            RequiresHumanApproval: true,
            RollbackPlan: "Remove reaction and script if fails or on explicit rollback.",
            Origin: "automation-main")
        {
            Sender = Self,
            Receiver = new NeuronId(SelfEvolutionNeuronIds.Main)
        }, cancellationToken);

        await FireAsync(new InoResponse(request.Prompt, $"Staged automation proposal {proposalId} (when={draft.When}).", []), cancellationToken);
        await DeliverAutomationProposalSurfaceAsync(proposalId, draft.Rationale, draft.When, draft.Script, request.ClientId, workspaceId, cancellationToken);
    }

    private static string DefaultAutomationTrigger(string prompt)
    {
        // Catalog + generic path only; triggers sourced from capability inventory and journals.
        return "NeuronActivated";
    }

    private static bool TryReadAutomationDraft(
        string raw,
        string defaultWhen,
        string defaultScript,
        string defaultRationale,
        out AutomationDraft draft)
    {
        draft = new AutomationDraft(defaultWhen, null, defaultScript, defaultRationale);
        var jsonText = ExtractJsonObject(raw);
        if (jsonText is null)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var when = defaultWhen;
            if (root.TryGetProperty("when", out var wEl) && wEl.ValueKind == JsonValueKind.String)
            {
                var candidate = wEl.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    if (!AllowedAutomationTriggers.Any(trigger => string.Equals(trigger, candidate, StringComparison.Ordinal)))
                    {
                        return false;
                    }

                    when = candidate;
                }
            }

            string? target = null;
            if (root.TryGetProperty("target", out var tEl) && tEl.ValueKind == JsonValueKind.String)
            {
                target = string.IsNullOrWhiteSpace(tEl.GetString()) ? null : tEl.GetString();
            }

            var script = defaultScript;
            if (root.TryGetProperty("script", out var scEl) && scEl.ValueKind == JsonValueKind.String)
            {
                var candidate = scEl.GetString();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    script = candidate;
                }
            }

            var rationale = defaultRationale;
            if (root.TryGetProperty("rationale", out var rEl) && rEl.ValueKind == JsonValueKind.String)
            {
                var candidate = rEl.GetString();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    rationale = candidate;
                }
            }

            draft = new AutomationDraft(when, target, script, rationale);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start
            ? raw.Substring(start, end - start + 1)
            : null;
    }

    private async Task DeliverLoginSurfaceAsync(string? clientId, CancellationToken cancellationToken = default)
    {
        var session = GrainFactory.GetGrain<IUserSessionNeuron>(IUserSessionNeuron.SingletonKey);
        var surface = await session.BuildLoginSurfaceAsync(clientId);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);
    }









    private async Task DeliverUiKitGallerySurfaceAsync(string? clientId, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var tree = UiKitGallery.Build("UiKit Gallery (via INO)");
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.Title] = "UiKit Gallery",
            [UiSurfaceKeys.SurfaceId] = "surface.uikit.gallery",
            ["role"] = "assistant",
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null)
        {
            props["clientId"] = clientId;
        }

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);
    }

    private async Task DeliverLlmSettingsSurfaceAsync(string? clientId, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);

        string current = "default (Aspire composition or global IChatClient)";
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store != null)
        {
            try
            {
                var sys = await store.GetAsync("system", "llm", cancellationToken);
                if (sys.TryGetValue("llm_provider", out var prov) && !string.IsNullOrWhiteSpace(prov))
                {
                    current = prov;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { /* optional */ }
        }

        var children = new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = "LLM / Model Settings" }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = $"Current active provider: {current}"
            }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "Current global selection is driven by 'system' pack config (llm_provider / llm_key) or Aspire composition default."
            }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "Supported: ollama (e.g. llama3.1:8b), azureopenai (gpt-4o-mini), openai, anthropic, github-models."
            }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "Click a button below to change (persisted to system/llm config and affects LlmResponder + Ino)."
            }),
            new(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Use Local Ollama",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = "set-llm:ollama",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }),
            new(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Use Azure OpenAI",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = "set-llm:azureopenai",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "Changes take effect immediately for new requests."
            })
        };

        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(), children),
            [UiSurfaceKeys.Title] = "LLM Settings",
            [UiSurfaceKeys.SurfaceId] = "surface.llm.settings",
            ["role"] = "assistant",
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null)
        {
            props["clientId"] = clientId;
        }

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);
    }

    private async Task HandleLlmSetCommandAsync(InoRequest request, string workspaceId, CancellationToken cancellationToken = default)
    {
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store == null)
        {
            await FireAsync(new InoResponse(request.Prompt, "No config store available to change LLM.", []), cancellationToken);
            return;
        }

        if (!TryParseLlmProviderCommand(request.Prompt, out var provider))
        {
            await FireAsync(new InoResponse(
                request.Prompt,
                "Unsupported LLM provider command. Use set-llm:ollama or set-llm:azureopenai.",
                []), cancellationToken);
            return;
        }

        string key = "";
        await store.SetAsync("system", "llm", new Dictionary<string, string> { ["llm_provider"] = provider, ["llm_key"] = key }, cancellationToken);

        await FireAsync(new InoResponse(request.Prompt, $"LLM provider set to {provider}.", []), cancellationToken);
        await DeliverReplySurfaceAsync($"Active LLM updated to {provider} via system config. New requests will use it.", request.ClientId, workspaceId, cancellationToken);

        // Refresh the settings surface so user sees the current value updated (feedback)
        await DeliverLlmSettingsSurfaceAsync(request.ClientId, workspaceId, cancellationToken);
        await CreateMemorySummaryAsync(workspaceId, cancellationToken);
    }

    private static bool TryParseLlmProviderCommand(string prompt, out string provider)
    {
        provider = "";
        var match = LlmProviderCommandRegex.Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        if (!LlmProviderCommands.TryGetValue(match.Groups["provider"].Value, out var resolved) ||
            string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        provider = resolved;
        return true;
    }

    private async Task HandleApproveProposalIntentAsync(InoRequest request, string workspaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var proposalId = TryExtractProposalId(request.Prompt);

        if (string.IsNullOrWhiteSpace(proposalId))
        {
            await DeliverReplySurfaceAsync("No explicit proposal id found to approve. Use 'approve proposal <proposal-id>'.", request.ClientId, workspaceId, cancellationToken);
            return;
        }

        var session = await ResolveSessionAsync(request.ClientId, cancellationToken);
        if (session is null)
        {
            await DeliverReplySurfaceAsync("Sign in before approving a self-evolution proposal.", request.ClientId, workspaceId, cancellationToken);
            return;
        }

        try
        {
            var approvalGrain = GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
            await approvalGrain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: session.UserId.Value, Reason: "Approved from Ino chat"), cancellationToken);
            await FireAsync(new InoResponse(request.Prompt, $"Approved proposal {proposalId}.", []), cancellationToken);
            await DeliverReplySurfaceAsync($"Proposal {proposalId} approved. It will activate if the apply handler succeeds.", request.ClientId, workspaceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to deliver approval decision for {Proposal}", proposalId);
            await DeliverReplySurfaceAsync($"Could not record approval for {proposalId}. Check self-evolution status.", request.ClientId, workspaceId, cancellationToken);
        }
    }

    private static string? TryExtractProposalId(string prompt)
    {
        var match = Regex.Match(
            prompt,
            @"\b(?<id>(?:automation|automation-remove|foundry|closedloop)-[A-Za-z0-9][A-Za-z0-9._-]*)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private async Task DeliverAutomationProposalSurfaceAsync(string proposalId, string rationale, string when, string script, string? clientId, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);

        var children = new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = "Automation ready as app" }),
            new(UiKitVocabulary.Tile, new Dictionary<string, object?>
            {
                ["title"] = proposalId,
                ["subtitle"] = $"Triggers on: {when}. {TrimForSurface(rationale)}"
            }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = "Preview script: " + TrimForSurface(script) }),
            new(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Run now (preview)",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = $"run automation {proposalId}",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }),
            new(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Approve & activate automation",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = $"approve proposal {proposalId}",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "Or chat: approve / run this automation. Ino can re-run automations too."
            })
        };

        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(), children),
            [UiSurfaceKeys.Title] = "Automation Proposal",
            [UiSurfaceKeys.SurfaceId] = $"surface.automation.proposal.{proposalId}",
            ["role"] = "assistant",
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null)
        {
            props["clientId"] = clientId;
        }

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);
    }

    private async Task DeliverGraphSurfaceAsync(UiWidgetTree tree, string? clientId, string? workspaceId, string title, string surfaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.Title] = title,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
            ["role"] = "assistant",
            ["surfaceKind"] = UiSurfaceKinds.GraphCanvas,
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null)
        {
            props["clientId"] = clientId;
        }

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>(IFlutterUiNeuron.SingletonKey);
        await flutter.DeliverAsync(StampCurrent(surface), cancellationToken);
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var result = await InteractAsync(new InoInteractRequest(prompt), cancellationToken);
        return result.ResponseText;
    }

    public async Task<InoInteractResult> InteractAsync(InoInteractRequest request, CancellationToken cancellationToken = default)
    {
        var clientId = request.ClientId;
        var workspaceId = WorkspaceIds.Effective(request.WorkspaceId);

        await FireAsync(new InoRequest(request.Prompt, clientId, workspaceId), cancellationToken);

        // Allow handlers (classifier, LLM, surface delivery, proposal staging) to run.
        // In real use, journals are the source of truth; this is the contract collector.
        await Task.Delay(50, cancellationToken);

        var tl = await GetOutgoingTimelineAsync(cancellationToken);
        var response = tl.OfType<InoResponse>().LastOrDefault();

        // Intent
        var classification = InoIntentClassifier.Classify(request.Prompt, await LoadCapabilityRecordsAsync(cancellationToken));

        // Recent memories for this scope
        var mems = OutgoingJournal
            .OfType<MemorySummary>()
            .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId)
            .TakeLast(request.MaxHistory)
            .ToList();

        // Pending proposals (the rail) - recent ones
        IReadOnlyList<SelfEvolutionProposalPending> proposals = Array.Empty<SelfEvolutionProposalPending>();
        if (request.IncludeProposals)
        {
            try
            {
                var se = GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
                proposals = (await se.GetTimelineAsync(cancellationToken))
                    .OfType<SelfEvolutionProposalPending>()
                    .TakeLast(request.MaxHistory)
                    .ToList();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { /* self-evo optional in some test setups */ }
        }

        // Available actions: derive from context + new architecture (automation proposals have Run/Approve)
        var actions = new List<InoAction>();
        if (request.IncludeActions)
        {
            // Generic follow-up
            actions.Add(new InoAction("Follow up with INO", FollowUpPrompt: "tell me more"));

            if (classification.Intent == "automation_create")
            {
                actions.Add(new InoAction("Run now (preview)", FollowUpPrompt: "run automation latest"));
                actions.Add(new InoAction("Approve & activate", FollowUpPrompt: "approve proposal latest"));
            }

            if (classification.Intent == "uikit_gallery")
            {
                actions.Add(new InoAction("Refresh gallery", FollowUpPrompt: "uikit gallery"));
            }
        }

        return new InoInteractResult(
            Prompt: request.Prompt,
            ResponseText: response?.Response ?? "processed",
            ClassifiedIntent: classification.Intent,
            IntentConfidence: classification.Confidence,
            ClientId: clientId,
            WorkspaceId: workspaceId,
            UsedTaskIds: response?.UsedTaskIds ?? Array.Empty<string>(),
            RecentMemoryTopics: mems.Select(m => m.Topic).ToList(),
            AvailableActions: actions,
            PendingProposals: proposals,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    private IReadOnlyList<ChatMessage> LoadConversationHistory(string? clientId)
    {
        var effectiveClientId = clientId ?? AnonymousClientId;
        return OutgoingJournal.Concat(IncomingJournal)
            .OfType<InoConversationTurn>()
            .Where(turn => turn.ClientId == effectiveClientId)
            // FireAsync self-delivers every fired synapse into both journals (same SynapseId), so without
            // this the same turn is read - and counted - twice.
            .DistinctBy(turn => turn.SynapseId)
            .OrderBy(turn => turn.Timestamp)
            .TakeLast(RecentConversationTurnsForContext)
            .Select(turn => new ChatMessage(
                string.Equals(turn.Role, ConversationTurnUserRole, StringComparison.Ordinal) ? ChatRole.User : ChatRole.Assistant,
                turn.Text))
            .ToList();
    }

    private async Task<string> BuildContextAsync(string prompt, string? workspaceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var recentOut = OutgoingJournal.TakeLast(RecentOutgoingForContext).ToList();
        var recentIn = IncomingJournal.TakeLast(RecentIncomingForContext).ToList();
        var completed = OutgoingJournal.OfType<TaskCompleted>().TakeLast(RecentCompletedTasksForContext).ToList();

        var mems = OutgoingJournal
            .OfType<MemorySummary>()
            .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId)
            .TakeLast(RecentMemoriesForContext)
            .ToList();

        var automations = OutgoingJournal.Concat(IncomingJournal)
            .OfType<AutomationDefinitionStaged>()
            .TakeLast(RecentAutomationsForContext)
            .ToList();

        var packet = InoContextPacketBuilder.Build(
            prompt,
            workspaceId,
            recentOut,
            recentIn,
            completed,
            mems,
            automations,
            await LoadCapabilityRecordsAsync(cancellationToken));

        await FireAsync(new ContextPacketSelected(packet.PacketId, workspaceId, packet.Evidence, packet.EstimatedSize), cancellationToken);
        return packet.RenderForPrompt();
    }

    private DbSchemaInspected? LatestSuccessfulSchema(string? clientId, string? workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var schemas = IncomingJournal
            .Concat(OutgoingJournal)
            .OfType<DbSchemaInspected>()
            .Where(schema => schema.Succeeded && schema.Schema is not null)
            .Where(schema => WorkspaceIds.Effective(schema.WorkspaceId ?? schema.Schema?.WorkspaceId) == workspaceId)
            .DistinctBy(schema => schema.SynapseId)
            .OrderBy(schema => schema.Timestamp)
            .ToList();

        if (schemas.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var clientMatch = schemas.LastOrDefault(schema => schema.ClientId == clientId);
            if (clientMatch is not null)
            {
                return clientMatch;
            }
        }

        return schemas[^1];
    }



    private static string TrimForSurface(string value)
    {
        var text = Regex.Replace(value.Trim(), @"\s+", " ");
        return text.Length <= 280 ? text : text[..277] + "...";
    }


    private static bool TryExtractDatabasePath(string prompt, out string path)
    {
        foreach (Match match in DatabasePathRegex().Matches(prompt))
        {
            var candidate = match.Value.Trim('"', '\'', ' ', '\t').TrimEnd('.', ',', ';', ')', ']');
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static Regex DatabasePathRegex() =>
        new(@"(?:""[^""]+\.(?:db|sqlite|sqlite3)""|'[^']+\.(?:db|sqlite|sqlite3)'|[A-Za-z]:\\[^\s""']+\.(?:db|sqlite|sqlite3))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


    private static string SchemaReplyText(DbSchemaInspected inspected) =>
        inspected.Succeeded && inspected.Schema is not null
            ? $"Rendered database schema for {inspected.Schema.SourcePath ?? inspected.ConnectionName}."
            : $"I could not inspect database schema '{inspected.ConnectionName}': {inspected.Error ?? "unknown error"}.";

    private static string SchemaMemorySummary(DbSchemaModel schema)
    {
        var objectCount = schema.Tables.Count;
        var columnCount = schema.Tables.Sum(table => table.Columns.Count);
        var fkCount = schema.Tables.Sum(table => table.ForeignKeys.Count);
        var indexCount = schema.Tables.Sum(table => table.Indexes.Count);
        var tables = string.Join("; ", schema.Tables.Select(table =>
            $"{table.Name}({string.Join(", ", table.Columns.Select(column => column.Name))})"));

        return $"Inspected SQLite schema '{schema.SourcePath ?? schema.ConnectionName}' with {objectCount} objects, {columnCount} columns, {fkCount} relationships, {indexCount} indexes. Tables: {tables}";
    }

    private static string StableSurfaceId(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var id = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(id) ? "sqlite" : id;
    }

    private async Task<string> ReasonWithLlmAsync(string prompt, string context, CancellationToken cancellationToken = default)
    {
        var chat = await ResolveGlobalLlmClientAsync(cancellationToken) ?? ServiceProvider.GetService<IChatClient>();
        if (chat == null)
        {
            return $"[no-llm] INO would act on: {SecretText.Redact(prompt)} (ctx len {context.Length})";
        }

        var sys = "You are INO, DigitalBrain's personal OS assistant. Use provided context from neuron journals. ALWAYS answer the user's request directly and visibly first with the actual content (e.g. the joke, summary, fact or help). Put any TASK: or BRANCH: directives ONLY on their own separate lines AFTER the answer, and ONLY if user explicitly asked to create a task/automation/branch. Never output only a directive. For a plain request like 'tell a joke' or 'generate a joke' just reply with the joke text directly.";
        var full = sys + "\nCTX:\n" + context + "\nUSER: " + SecretText.Redact(prompt);
        var (text, _) = await GetChatTextOrFallbackAsync(chat, full, cancellationToken);
        return text;
    }

    // TaskCanceledException also covers the Ollama-model-still-loading case: OllamaApiClient's HttpClient has
    // no explicit timeout configured, so a slow model pull/load times out via HttpClient's default timeout
    // rather than failing fast with a connection-refused HttpRequestException.
    private async Task<(string Text, bool Available)> GetChatTextOrFallbackAsync(IChatClient chat, string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await chat.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            var text = response.Text.Trim();
            return (string.IsNullOrWhiteSpace(text) ? "I do not have a useful answer yet." : text, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Logger.LogWarning(ex, "INO LLM request failed; returning an unavailable response.");
            return (LlmUnavailableReply, false);
        }
    }

    // The generic tool-calling path needs a model that actually supports native function-calling, not just
    // whatever the flat unkeyed default happens to be. Picks the first registry entry flagged SupportsTools
    // and resolves its keyed IChatClient.
    private async Task<IChatClient?> ResolveToolCapableChatClientAsync(CancellationToken cancellationToken)
    {
        var config = ServiceProvider.GetService<IConfiguration>();
        if (config is null)
        {
            return null;
        }

        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
        var toolCapable = DigitalBrainModelRegistrySnapshot.FirstOrDefault(
            entries, DigitalBrainCapabilityKind.LargeLanguageModel, e => e.Capabilities.SupportsTools);
        if (toolCapable is null || string.IsNullOrWhiteSpace(toolCapable.ServiceKey))
        {
            return null;
        }

        return ServiceProvider.GetKeyedService<IChatClient>(toolCapable.ServiceKey);
    }

    private async Task<IChatClient?> ResolveGlobalLlmClientAsync(CancellationToken cancellationToken = default)
    {
        var factory = ServiceProvider.GetService<IScopedChatClientFactory>();
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (factory is null || store is null)
        {
            return null;
        }

        try
        {
            var sys = await store.GetAsync("system", "llm", cancellationToken);
            if (sys.TryGetValue("llm_provider", out var provider) && !string.IsNullOrWhiteSpace(provider))
            {
                sys.TryGetValue("llm_key", out var key);
                return factory.Create(provider, string.IsNullOrWhiteSpace(key) ? null : key);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch { /* optional */ }
        return null;
    }

    private async Task CreateMemorySummaryAsync(string? workspaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        cancellationToken.ThrowIfCancellationRequested();
        var recent = OutgoingJournal.Concat(IncomingJournal).TakeLast(RecentCombinedForMemorySummary).ToList();
        if (recent.Count < MinJournalsForMemorySummary)
        {
            return;
        }

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null)
        {
            return;
        }

        var ctx = string.Join("\n", recent.Select(s => s.Type + ": " + SecretText.Redact(s.ToString() ?? string.Empty)));
        var prompt = "Summarize the following recent activity in DigitalBrain for personal assistant memory. One short topic + 1-sentence summary. Activity:\n" + ctx;
        var (summaryText, available) = await GetChatTextOrFallbackAsync(chat, prompt, cancellationToken);
        if (!available)
        {
            return;
        }

        if (summaryText.Length > 10)
        {
            var sanitizedSummary = SecretText.Redact(summaryText);
            var topic = sanitizedSummary.Split('.')[0].Trim();
            var mem = new MemorySummary(topic.Length > 30 ? topic.Substring(0, 30) : topic, sanitizedSummary, DateTimeOffset.UtcNow, workspaceId, "ActivitySummary", "JournalFact", "InoNeuron");
            await FireAsync(mem, cancellationToken);
        }
    }

}

