using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Ino.Context;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using DigitalBrain.Kernel;
using DigitalBrain.Ui.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Ino;

using DigitalBrain.Ui.Contracts;

// INO: ultra-context personal assistant neuron.
// Uses dual journals as primary memory (recent + full history), spawns KernelTasks for actions,
// can drive checkpoints/branches for planning. Context is multi-scale via recency + LLM summary.
[GrainType("ino.personal.v1")]
public class InoNeuron(ILogger<InoNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IInoNeuron, IHandle<Signal>
{
    private sealed record ReplyPlan(string VisibleReply, IReadOnlyList<string> TaskDescriptions, string? BranchDescription);
    private sealed record GmailMessageSummary(string Id, string Body);

    private InoRequest? _pendingGmailRequest;
    private InoRequest? _pendingSalesforceRequest;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        LoadCapabilitiesFromJournal();
        await RememberCapabilitiesAsync();
    }

    private async Task RememberCapabilitiesAsync()
    {
        try
        {
            var context = GrainFactory.GetGrain<IContextNeuron>("context-main");
            foreach (var cap in InoIntentClassifier.Capabilities)
            {
                var text = $"capability:{cap.Id} {cap.Description} examples:{string.Join(" ", cap.Examples)} tier:{cap.Tier}";
                // Remember will embed via Context and store as MemoryStored for vector recall
                await context.RememberAsync(text);
            }
        }
        catch { /* context optional for classifier grounding */ }
    }

    private void LoadCapabilitiesFromJournal()
    {
        var regs = OutgoingJournal.Concat(IncomingJournal)
            .OfType<CapabilityRegistered>();
        foreach (var reg in regs)
        {
            InoIntentClassifier.RegisterCapability(new InoIntentClassifier.Capability(
                reg.Id, reg.Description, reg.Examples.ToArray(), reg.Tier));
        }
    }

    public async Task HandleAsync(InoRequest req)
    {
        var workspaceId = WorkspaceIds.Effective(req.WorkspaceId);

        // Check for gallery early (before generic handler which always matches)
        var cls = await InoIntentClassifier.ClassifyWithLlmAsync(req.Prompt, ServiceProvider);
        if (cls.Intent == "uikit_gallery")
        {
            await DeliverUiKitGallerySurfaceAsync(req.ClientId, workspaceId);
            await FireAsync(new InoResponse(req.Prompt, "UiKit component gallery:", []));
            return;
        }

        // Handle set-llm commands from settings surface buttons (they classify as llm_settings due to "llm" keyword, so check sets first)
        var pForCheck = req.Prompt.ToLowerInvariant();
        if (pForCheck.Contains("set-llm") || pForCheck.Contains("use qwen") || pForCheck.Contains("use local") || pForCheck.Contains("use gpt") || pForCheck.Contains("use azure"))
        {
            await HandleLlmSetCommandAsync(req, workspaceId);
            return;
        }

        // Approve via chat: "approve proposal <id>" or "approve that automation" routes to decision (rail)
        if (pForCheck.Contains("approve") && (pForCheck.Contains("proposal") || pForCheck.Contains("automation") || pForCheck.Contains("self-evolution")))
        {
            await HandleApproveProposalIntentAsync(req, workspaceId);
            return;
        }

        if (cls.Intent == "llm_settings")
        {
            await DeliverLlmSettingsSurfaceAsync(req.ClientId, workspaceId);
            await FireAsync(new InoResponse(req.Prompt, "LLM / model settings:", []));
            return;
        }

        if (cls.Intent == "automation_create")
        {
            await HandleAutomationCreateIntentAsync(req, workspaceId);
            return;
        }

        var p = req.Prompt.ToLowerInvariant();
        if (p.Contains("run automation") || p.Contains("run now") || p.Contains("execute automation"))
        {
            var reply = "Running the requested automation (preview or activated). Check the Tasks surface for results.";
            await FireAsync(new InoResponse(req.Prompt, reply, []));
            await DeliverReplySurfaceAsync(reply, req.ClientId, workspaceId);
            return;
        }

        foreach (var handler in InoIntentHandlers.Default)
        {
            if (await handler.TryHandleAsync(this, req, workspaceId))
            {
                return;
            }
        }
    }

    internal async Task HandleRelationGraphIntentAsync(InoRequest req, string workspaceId)
    {
        await FireAsync(new InoResponse(req.Prompt, "Rendered a relation graph.", []));
        await DeliverGraphSurfaceAsync(
            DbSchemaGraphMapper.RelationOfTwoObjectsTree(),
            req.ClientId,
            workspaceId,
            "Object relation",
            "surface.graph.relation");
    }

    internal async Task<bool> TryHandleSchemaVisualizationIntentAsync(InoRequest req, string workspaceId)
    {
        if (TryExtractDatabasePath(req.Prompt, out var databasePath))
        {
            var inspected = await InspectReferencedDatabaseAsync(databasePath, req.ClientId, workspaceId);
            if (inspected is not null)
            {
                await FireAsync(new InoResponse(req.Prompt, SchemaReplyText(inspected), []));
                await FireAsync(inspected);
                return true;
            }
        }

        var latest = LatestSuccessfulSchema(req.ClientId, workspaceId);
        if (latest?.Schema is not null)
        {
            await FireAsync(new InoResponse(req.Prompt, "Rendered the most recent database schema.", []));
            await ProcessSchemaInspectedAsync(latest, req.ClientId ?? latest.ClientId, workspaceId);
            return true;
        }

        return false;
    }

    internal async Task HandleGenericIntentAsync(InoRequest req, string workspaceId)
    {
        var p = req.Prompt.ToLowerInvariant();
        bool gmailFollowupSummarize = (p.Contains("summar") || p.Contains("brief") || p.Contains("what was")) &&
            (p.Contains("last") || p.Contains("previous") || p.Contains("that") || p.Contains("it") || p.Contains("the one") || p.Contains("previous one")) &&
            (p.Contains("email") || p.Contains("gmail") || p.Contains("mail"));

        if (!gmailFollowupSummarize)
        {
            // Cross-turn: "summarize that one" after prior gmail context in journal (no keyword required in this turn)
            var lastGmailish = IncomingJournal.OfType<InoRequest>().TakeLast(3).Any(r => InoConnectorIntents.IsGmail(r.Prompt));
            var hasGmailMem = IncomingJournal.Concat(OutgoingJournal).OfType<MemorySummary>()
                .TakeLast(5).Any(m => (m.Topic ?? "").ToLowerInvariant().Contains("gmail") || (m.Topic ?? "").ToLowerInvariant().Contains("email"));
            if ((p.Contains("summar") || p.Contains("brief")) && (lastGmailish || hasGmailMem))
                gmailFollowupSummarize = true;
        }

        if (gmailFollowupSummarize)
        {
            var lastBodies = GetLastGmailBodiesFromJournal(workspaceId);
            if (!string.IsNullOrWhiteSpace(lastBodies))
            {
                var sum = await ReasonWithLlmAsync("Provide a concise 3-5 bullet point summary of these emails (key points only):\n" + lastBodies, "");
                await FireAsync(new InoResponse(req.Prompt, "Summary of last Gmail: " + sum, []));
                await DeliverReplySurfaceAsync("Summary of previous Gmail messages:\n" + sum, req.ClientId, workspaceId);
                await CreateMemorySummaryAsync(workspaceId);
                return;
            }
        }

        // Cross G->SF followup via generic (if classify didn't route to salesforce handler)
        bool crossGmailToSf = (p.Contains("salesforce") || p.Contains("crm") || p.Contains("account")) &&
                              (p.Contains("last email") || p.Contains("previous email") || p.Contains("related") || p.Contains("from the email"));
        if (crossGmailToSf)
        {
            var lastGmail = GetLastGmailBodiesFromJournal(workspaceId);
            if (!string.IsNullOrWhiteSpace(lastGmail))
            {
                var suggestion = await ReasonWithLlmAsync("Relate this Gmail to Salesforce context. Email:\n" + lastGmail + "\nRequest: " + req.Prompt, "");
                await FireAsync(new InoResponse(req.Prompt, "Cross Gmail->SF (journal): " + suggestion, []));
                await DeliverReplySurfaceAsync(suggestion, req.ClientId, workspaceId);
                await CreateMemorySummaryAsync(workspaceId);
                return;
            }
        }

        // Fallback for text "set llm" prompts (button sets are handled early via HandleLlmSetCommandAsync)
        if (p.Contains("set-llm") || p.Contains("use qwen") || p.Contains("use local") || p.Contains("use gpt") || p.Contains("use azure"))
        {
            await HandleLlmSetCommandAsync(req, workspaceId);
            return;
        }

        var ctx = await BuildContextAsync(req.Prompt, workspaceId);
        var rawReply = await ReasonWithLlmAsync(req.Prompt, ctx);
        var replyPlan = BuildReplyPlan(req.Prompt, rawReply);
        // Always ensure a clean direct visible answer for the user (fixes cases where LLM emits only TASK/BRANCH or mixes for simple asks like jokes).
        // Tasks/Branches still get orchestrated from the plan if present.
        if (string.IsNullOrWhiteSpace(replyPlan.VisibleReply) || replyPlan.TaskDescriptions.Count > 0 || !string.IsNullOrWhiteSpace(replyPlan.BranchDescription))
        {
            var directReply = await ReasonDirectlyWithLlmAsync(req.Prompt, ctx);
            replyPlan = replyPlan with { VisibleReply = directReply };
        }

        var taskIds = await OrchestrateActionsIfNeededAsync(replyPlan);

        await FireAsync(new InoResponse(req.Prompt, replyPlan.VisibleReply, taskIds.ToArray()));
        await DeliverReplySurfaceAsync(replyPlan.VisibleReply, req.ClientId, workspaceId);

        // Compress recent activity to long-term memory summary (journal driven).
        await CreateMemorySummaryAsync(workspaceId);
    }
    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name == "PackConfigured" &&
            signal.Props.TryGetValue("pack", out var pack) &&
            string.Equals(pack?.ToString(), SalesforceClientFactory.PackName, StringComparison.OrdinalIgnoreCase))
        {
            var pendingSalesforce = _pendingSalesforceRequest
                ?? IncomingJournal.OfType<InoRequest>().LastOrDefault(r => InoConnectorIntents.IsSalesforce(r.Prompt));

            if (pendingSalesforce is not null)
            {
                var salesforceUserId = await ResolveUserIdAsync(pendingSalesforce.ClientId);
                if (await HasSalesforceCredentialAsync(salesforceUserId))
                {
                    _pendingSalesforceRequest = null;
                    await FetchSalesforceAccountsAsync(pendingSalesforce, salesforceUserId);
                }
            }

            return;
        }

        if (signal.Name != GoogleSignals.AuthCompleted)
            return;

        var pending = _pendingGmailRequest
            ?? IncomingJournal.OfType<InoRequest>().LastOrDefault(r => InoConnectorIntents.IsGmail(r.Prompt));

        if (pending is null || !await HasGoogleCredentialAsync())
            return;

        _pendingGmailRequest = null;
        await FetchRecentGmailAsync(pending);
    }

    private async Task<string> ResolveUserIdAsync(string? clientId)
    {
        var state = await ResolveSessionAsync(clientId);
        return state?.UserId.Value ?? UserId.Anonymous.Value;
    }

    private async Task<UserSessionState?> ResolveSessionAsync(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var session = GrainFactory.GetGrain<IUserSessionNeuron>("session-main");
        return await session.GetSessionByClientIdAsync(clientId);
    }

    public async Task HandleAsync(TabularDataIngested ingested)
    {
        var workspaceId = WorkspaceIds.Effective(ingested.WorkspaceId);
        var headers = JsonSerializer.Deserialize<List<string>>(ingested.HeadersJson) ?? [];
        var rows = JsonSerializer.Deserialize<List<List<string>>>(ingested.RowsJson) ?? [];

        var tree = new UiWidgetTree(UiKitVocabulary.Panel, new Dictionary<string, object?>(), new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = ingested.FileName }),
            new(UiKitVocabulary.Table, new Dictionary<string, object?> { ["columns"] = headers, ["rows"] = rows }),
        });

        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.Title] = "INO",
            ["role"] = "assistant",
        };
        if (ingested.ClientId is not null) props["clientId"] = ingested.ClientId;
        props["workspaceId"] = workspaceId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));

        // Deterministic (not LLM-generated) so follow-up questions can find this data via BuildContextAsync
        // even when no IChatClient is configured (the [no-llm] fallback path).
        var summary = $"Uploaded '{ingested.FileName}' with columns [{string.Join(", ", headers)}] and {rows.Count} data rows. Column stats: {ingested.ColumnStatsJson}";
        await FireAsync(new MemorySummary(ingested.FileName, summary, DateTimeOffset.UtcNow, workspaceId));
    }

    public Task HandleAsync(DbSchemaInspected inspected) =>
        ProcessSchemaInspectedAsync(inspected, inspected.ClientId, inspected.WorkspaceId);

    private async Task ProcessSchemaInspectedAsync(DbSchemaInspected inspected, string? clientId, string? workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        if (!inspected.Succeeded || inspected.Schema is null)
        {
            var message = $"I could not inspect database schema '{inspected.ConnectionName}': {inspected.Error ?? "unknown error"}.";
            await DeliverReplySurfaceAsync(message, clientId, workspaceId);
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
            "surface.db-schema." + StableSurfaceId(schema.ConnectionName));

        await FireAsync(new MemorySummary(
            schema.ConnectionName,
            SchemaMemorySummary(schema),
            DateTimeOffset.UtcNow,
            workspaceId));
    }

    private async Task<DbSchemaInspected?> InspectReferencedDatabaseAsync(string databasePath, string? clientId, string? workspaceId)
    {
        var connectionName = Path.GetFileNameWithoutExtension(databasePath);
        if (string.IsNullOrWhiteSpace(connectionName))
            connectionName = "sqlite-db";

        workspaceId = WorkspaceIds.Effective(workspaceId);
        var cmd = new DbInspectSchema(connectionName, "sqlite", SourcePath: databasePath, ClientId: clientId, WorkspaceId: workspaceId);
        var db = GrainFactory.GetGrain<IDbSupportNeuron>("db-main");
        await db.FireAsync(cmd);

        var timeline = await db.GetTimelineAsync();
        return timeline
            .OfType<DbSchemaInspected>()
            .LastOrDefault(result => result.CorrelationId == cmd.SynapseId)
            ?? timeline.OfType<DbSchemaInspected>().LastOrDefault(result => result.ConnectionName == connectionName);
    }

    private async Task DeliverReplySurfaceAsync(string reply, string? clientId, string? workspaceId = null)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = reply }),
            [UiSurfaceKeys.Title] = "INO",
            ["role"] = "assistant",
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    internal async Task HandleGmailIntentAsync(InoRequest req)
    {
        var workspaceId = WorkspaceIds.Effective(req.WorkspaceId);

        // Use LLM classifier for natural language understanding (beyond simple keywords).
        // Falls back gracefully if no LLM.
        var cls = await InoIntentClassifier.ClassifyWithLlmAsync(req.Prompt, ServiceProvider);
        if (cls.Intent != "gmail")
        {
            // Not confident enough after LLM — fall through to generic in caller if needed.
            await HandleGenericIntentAsync(req, workspaceId);
            return;
        }

        var p = req.Prompt.ToLowerInvariant();
        bool isSummarizeFollowup = (p.Contains("summar") || p.Contains("brief") || p.Contains("what was")) &&
                                   (p.Contains("last") || p.Contains("previous") || p.Contains("that") || p.Contains("it") || p.Contains("the one") || p.Contains("previous one"));

        if (isSummarizeFollowup)
        {
            var lastBodies = GetLastGmailBodiesFromJournal(workspaceId);
            if (!string.IsNullOrWhiteSpace(lastBodies))
            {
                var sum = await ReasonWithLlmAsync("Provide a concise 3-5 bullet point summary of these emails (key points only):\n" + lastBodies, "");
                await FireAsync(new InoResponse(req.Prompt, "Summary of last Gmail: " + sum, []));
                await DeliverReplySurfaceAsync("Summary of previous Gmail messages:\n" + sum, req.ClientId, workspaceId);
                return;
            }
        }

        if (!await HasGoogleCredentialAsync())
        {
            _pendingGmailRequest = req;
            var reply = "Google authentication is required to read Gmail.";
            await FireAsync(new InoResponse(req.Prompt, reply, []));
            await DeliverGoogleAuthSurfaceAsync(req.ClientId, workspaceId);
            return;
        }

        await FetchRecentGmailAsync(req);
    }

    internal async Task HandleSalesforceIntentAsync(InoRequest req)
    {
        var workspaceId = WorkspaceIds.Effective(req.WorkspaceId);

        var cls = await InoIntentClassifier.ClassifyWithLlmAsync(req.Prompt, ServiceProvider);
        if (cls.Intent != "salesforce")
        {
            await HandleGenericIntentAsync(req, workspaceId);
            return;
        }

        var p = req.Prompt.ToLowerInvariant();
        bool isSummarizeFollowup = (p.Contains("summar") || p.Contains("brief") || p.Contains("what was")) &&
                                   (p.Contains("last") || p.Contains("previous") || p.Contains("that") || p.Contains("it") || p.Contains("the one") || p.Contains("previous one"));

        if (isSummarizeFollowup)
        {
            var last = GetLastSalesforceFromJournal(workspaceId);
            if (!string.IsNullOrWhiteSpace(last))
            {
                var sum = await ReasonWithLlmAsync("Provide a concise summary of these Salesforce accounts:\n" + last, "");
                await FireAsync(new InoResponse(req.Prompt, "Summary of last Salesforce: " + sum, []));
                await DeliverReplySurfaceAsync("Summary of previous Salesforce data:\n" + sum, req.ClientId, workspaceId);
                return;
            }
        }

        // Richer G/SF follow-up: "related to last email" / cross from Gmail journal without needing SF cred/fetch
        bool isGmailRelatedSf = (p.Contains("related") || p.Contains("from the email") || p.Contains("last email") || p.Contains("previous email")) &&
                                (p.Contains("salesforce") || p.Contains("crm") || p.Contains("account"));
        if (isGmailRelatedSf)
        {
            var lastGmail = GetLastGmailBodiesFromJournal(workspaceId);
            if (!string.IsNullOrWhiteSpace(lastGmail))
            {
                var suggestion = await ReasonWithLlmAsync(
                    "User wants Salesforce CRM info related to this recent Gmail. Last email content:\n" + lastGmail + "\n\nUser request: " + req.Prompt +
                    "\n\nProvide a concise helpful response (suggest matching accounts/topics, key names/companies from the email). If fetching live data is needed later, note it.", "");
                await FireAsync(new InoResponse(req.Prompt, "Related to last email (using journal): " + suggestion, []));
                await DeliverReplySurfaceAsync("Based on previous Gmail + Salesforce context:\n" + suggestion, req.ClientId, workspaceId);
                await CreateMemorySummaryAsync(workspaceId);
                return;
            }
        }

        var salesforceSession = await ResolveSessionAsync(req.ClientId);
        if (salesforceSession is null)
        {
            var reply = "Sign in before connecting Salesforce.";
            await FireAsync(new InoResponse(req.Prompt, reply, []));
            await DeliverLoginSurfaceAsync(req.ClientId);
            return;
        }

        var salesforceUserId = salesforceSession.UserId.Value;
        if (!await HasSalesforceCredentialAsync(salesforceUserId))
        {
            _pendingSalesforceRequest = req;
            var reply = "Salesforce credentials are required to query CRM records.";
            await FireAsync(new InoResponse(req.Prompt, reply, []));
            await DeliverSalesforceCredentialSurfaceAsync(req.ClientId, workspaceId);
            return;
        }

        await FetchSalesforceAccountsAsync(req, salesforceUserId);
    }

    internal async Task HandleAutomationCreateIntentAsync(InoRequest req, string workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);

        // Structured LLM extraction for high quality automation (Gmail/SF examples supported).
        // Use direct chat for clean JSON output (generic Reason wrapper may add prose).
        string raw;
        var chat = await ResolveGlobalLlmClientAsync() ?? ServiceProvider.GetService<IChatClient>();
        if (chat is not null)
        {
            var specPrompt =
                "You are a precise automation designer for DigitalBrain. " +
                "Turn the user request into a safe reaction. " +
                "Reply with ONLY minified JSON and nothing else (no code fences, no prose):\n" +
                "{\"when\":\"Signal:GmailMessageReceived\",\"target\":null,\"script\":\"return new[] { new Signal(\\\"EmailSummarized\\\", new Dictionary<string,object?>{[\\\"summary\\\"]=\\\"...\\\"}) }; \",\"rationale\":\"short reason\"}\n" +
                "Rules: when must be Signal:GmailMessageReceived (for gmail), Signal:SalesforceQueryReady (for sf/crm), or NeuronActivated. " +
                "script: short safe C# returning Signal[] (example uses realistic emitted signals for follow-on G/SF glue). No file system, loops or unsafe. " +
                "User request: " + req.Prompt;
            var resp = await chat.GetResponseAsync(specPrompt);
            raw = resp.Text?.Trim() ?? "";
        }
        else
        {
            var ctx = await BuildContextAsync(req.Prompt, workspaceId);
            var llmPrompt = "You are helping create a safe DigitalBrain automation. Output ONLY the JSON: {\"when\":\"...\",\"target\":null,\"script\":\"...\",\"rationale\":\"...\"}. User: " + req.Prompt;
            raw = await ReasonWithLlmAsync(llmPrompt, ctx);
        }

        // Robust parse preferring JSON, with keyword fallback for G/SF.
        string when = "NeuronActivated";
        string? target = null;
        string script = "return new[] { new Signal(\"AutomationFired\", new Dictionary<string,object?> { [\"desc\"] = \"from chat\" }) };";
        string rationale = $"Automation proposed from: {req.Prompt}";

        // Try JSON first (supports structured output from LLM)
        try
        {
            var jsonText = raw;
            if (jsonText.Contains("```"))
            {
                // strip common fences
                var start = jsonText.IndexOf('{');
                var end = jsonText.LastIndexOf('}');
                if (start >= 0 && end > start) jsonText = jsonText.Substring(start, end - start + 1);
            }
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.TryGetProperty("when", out var wEl) && wEl.ValueKind == JsonValueKind.String)
                when = wEl.GetString() ?? when;
            if (root.TryGetProperty("target", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                target = tEl.GetString();
            if (root.TryGetProperty("script", out var scEl) && scEl.ValueKind == JsonValueKind.String)
                script = scEl.GetString() ?? script;
            if (root.TryGetProperty("rationale", out var rEl) && rEl.ValueKind == JsonValueKind.String)
                rationale = rEl.GetString() ?? rationale;
        }
        catch
        {
            // Fallbacks for G/SF and crude extract
            if (raw.Contains("Gmail", StringComparison.OrdinalIgnoreCase) || raw.Contains("email", StringComparison.OrdinalIgnoreCase))
                when = "Signal:GmailMessageReceived";
            else if (raw.Contains("salesforce", StringComparison.OrdinalIgnoreCase) || raw.Contains("crm", StringComparison.OrdinalIgnoreCase))
                when = "Signal:SalesforceQueryReady";

            if (raw.Contains("return", StringComparison.OrdinalIgnoreCase))
            {
                var start = raw.IndexOf("return", StringComparison.OrdinalIgnoreCase);
                var end = raw.IndexOf(';', start);
                if (end > start) script = raw.Substring(start, end - start + 1).Trim();
            }
            if (raw.Contains("\"when\"", StringComparison.OrdinalIgnoreCase))
            {
                var wmatch = System.Text.RegularExpressions.Regex.Match(raw, @"""when""\s*:\s*""([^""]+)""");
                if (wmatch.Success) when = wmatch.Groups[1].Value;
            }
        }

        var autoId = "chat-auto-" + Guid.NewGuid().ToString("N")[..8];
        var proposalId = "automation-" + Guid.NewGuid().ToString("N");
        var scriptId = autoId + "-script";
        var regScript = new RegisterScript(scriptId, script, "via-ino-chat", Array.Empty<string>(), "default");
        var regReaction = new RegisterReaction(autoId, when, scriptId, target, Array.Empty<string>(), "default");

        var autoGrain = GrainFactory.GetGrain<IAutomationNeuron>("automation-main");
        await autoGrain.FireAsync(new AutomationDefinitionStaged(proposalId, "automation-main", regScript, regReaction));

        var approval = GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionProposal(
            ProposalId: proposalId,
            Scope: "automation:default",
            Rationale: rationale,
            ProposedChange: $"Register automation {autoId} (when={when})",
            ApplyVia: SelfEvolutionApplyVia.AutomationDefineReaction,
            Risk: SelfEvolutionRisk.InProcessCode,
            RequiresHumanApproval: true,
            RollbackPlan: "Remove reaction and script if fails or on explicit rollback.",
            Origin: "automation-main")
        {
            Sender = Self,
            Receiver = new NeuronId(SelfEvolutionNeuronIds.Main)
        });

        await FireAsync(new InoResponse(req.Prompt, $"Staged automation proposal {proposalId} (when={when}).", []));
        await DeliverAutomationProposalSurfaceAsync(proposalId, rationale, when, script, req.ClientId, workspaceId);
    }

    private async Task<bool> HasGoogleCredentialAsync()
    {
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store is null)
            return false;

        try
        {
            // Wire GetMergedScopedValuesAsync so tokens written to user scope are seen (root cause B).
            var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, Self.AsScope());
            return GoogleClientFactory.HasUsableCredential(values);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Google credential check failed.");
            return false;
        }

        static bool HasValue(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    private async Task<bool> HasSalesforceCredentialAsync(string userId)
    {
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store is null)
            return false;

        try
        {
            var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, new NeuronScope(new UserId(userId), null));
            return SalesforceClientFactory.HasUsableCredential(merged);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Salesforce credential check failed.");
            return false;
        }
    }

    private async Task DeliverGoogleAuthSurfaceAsync(string? clientId, string? workspaceId = null)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var tree = new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(), new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "Connect Google to let INO read your recent Gmail messages."
            }),
            new(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Authenticate Google",
                ["icon"] = "gmail",
                ["synapseType"] = GoogleSignals.AuthRequested
            })
        });

        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.Title] = "Google",
            [UiSurfaceKeys.SurfaceId] = "surface.google-auth.gmail",
            ["role"] = "assistant",
            ["surfaceKind"] = UiSurfaceKinds.AuthButton,
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task DeliverLoginSurfaceAsync(string? clientId)
    {
        var session = GrainFactory.GetGrain<IUserSessionNeuron>("session-main");
        var surface = await session.BuildLoginSurfaceAsync(clientId);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task DeliverSalesforceCredentialSurfaceAsync(string? clientId, string? workspaceId = null)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var surface = SalesforceAuthSurfaces.CredentialForm(Self.Value, clientId);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task FetchRecentGmailAsync(InoRequest req)
    {
        var workspaceId = WorkspaceIds.Effective(req.WorkspaceId);
        var cls = await InoIntentClassifier.ClassifyWithLlmAsync(req.Prompt, ServiceProvider);
        var maxResults = cls.MaxResults ?? InoConnectorIntents.ResultCount(req.Prompt);
        var q = cls.Query ?? "";
        await Broadcast(new Signal(GoogleSignals.GmailFetchRequested, new Dictionary<string, object?>
        {
            ["prompt"] = req.Prompt,
            ["clientId"] = req.ClientId,
            ["workspaceId"] = workspaceId,
            ["maxResults"] = maxResults,
            ["query"] = q
        }));

        var gmail = GrainFactory.GetGrain<IGmailNeuron>("gmail-main");
        var ids = await gmail.ListMessagesAsync(q, maxResults);
        var summaries = new List<GmailMessageSummary>();

        foreach (var id in ids.Take(maxResults))
        {
            var body = await gmail.ReadMessageAsync(id);
            summaries.Add(new GmailMessageSummary(id, body));
        }

        await Broadcast(new Signal(GoogleSignals.GmailMessagesReady, new Dictionary<string, object?>
        {
            ["clientId"] = req.ClientId,
            ["workspaceId"] = workspaceId,
            ["count"] = summaries.Count,
            ["messageIds"] = string.Join(",", summaries.Select(m => m.Id))
        }));

        var reply = GmailReplyText(summaries);
        await FireAsync(new InoResponse(req.Prompt, reply, []));

        if (summaries.Count > 0)
        {
            var bodies = string.Join("\n---\n", summaries.Select(s => s.Body));
            await FireAsync(new MemorySummary("last-gmail", bodies, DateTimeOffset.UtcNow, workspaceId));
        }

        string? summary = null;
        var p = req.Prompt.ToLowerInvariant();
        if ((p.Contains("summar") || p.Contains("brief")))
        {
            string bodiesToSummarize = summaries.Count > 0
                ? string.Join("\n---\n", summaries.Select(s => s.Body))
                : GetLastGmailBodiesFromJournal(workspaceId) ?? "";
            if (!string.IsNullOrWhiteSpace(bodiesToSummarize))
            {
                summary = await ReasonWithLlmAsync("Provide a concise 3-5 bullet point summary of these emails (key points only):\n" + bodiesToSummarize, "");
            }
        }

        await DeliverGmailMessagesSurfaceAsync(summaries, req.ClientId, workspaceId, summary);
    }

    private async Task FetchSalesforceAccountsAsync(InoRequest req, string salesforceUserId)
    {
        var workspaceId = WorkspaceIds.Effective(req.WorkspaceId);
        var cls = await InoIntentClassifier.ClassifyWithLlmAsync(req.Prompt, ServiceProvider);
        var maxResults = cls.MaxResults ?? InoConnectorIntents.ResultCount(req.Prompt);
        await Broadcast(new Signal(SalesforceSignals.QueryRequested, new Dictionary<string, object?>
        {
            ["prompt"] = req.Prompt,
            ["clientId"] = req.ClientId,
            ["workspaceId"] = workspaceId,
            ["maxResults"] = maxResults
        }));

        string[] records;
        try
        {
            var salesforce = GrainFactory.GetGrain<ISalesforceCrmNeuron>(salesforceUserId);
            records = await salesforce.ListAccountsAsync(maxResults);
        }
        catch (Exception ex) when (IsSalesforceIntegrationFailure(ex))
        {
            Logger.LogWarning(ex, "Salesforce query failed after credentials were configured.");
            var failureReply = SalesforceFailureReply(ex);
            await FireAsync(new InoResponse(req.Prompt, failureReply, []));
            await DeliverReplySurfaceAsync(failureReply, req.ClientId, workspaceId);
            await DeliverSalesforceCredentialSurfaceAsync(req.ClientId, workspaceId);
            return;
        }

        await Broadcast(new Signal(SalesforceSignals.QueryResultsReady, new Dictionary<string, object?>
        {
            ["clientId"] = req.ClientId,
            ["workspaceId"] = workspaceId,
            ["count"] = records.Length
        }));

        var reply = SalesforceReplyText(records);
        await FireAsync(new InoResponse(req.Prompt, reply, []));

        if (records.Length > 0)
        {
            await FireAsync(new MemorySummary("last-salesforce", string.Join("\n", records), DateTimeOffset.UtcNow, workspaceId));
        }

        await DeliverSalesforceRecordsSurfaceAsync(records, req.ClientId, workspaceId);
    }

    private async Task DeliverGmailMessagesSurfaceAsync(IReadOnlyList<GmailMessageSummary> messages, string? clientId, string? workspaceId = null, string? summary = null)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var children = new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = "Recent Gmail" })
        };

        if (summary != null)
        {
            children.Add(new UiWidgetTree(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = "Summary" }));
            children.Add(new UiWidgetTree(UiKitVocabulary.TextArea, new Dictionary<string, object?>
            {
                ["text"] = summary
            }));
        }

        if (messages.Count == 0)
        {
            children.Add(new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "No recent Gmail messages were returned."
            }));
        }
        else
        {
            var listItems = new List<UiWidgetTree>();
            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                // Use Tile for richer per-message UI
                listItems.Add(new UiWidgetTree(UiKitVocabulary.Tile, new Dictionary<string, object?>
                {
                    ["title"] = $"Message {i + 1}",
                    ["subtitle"] = TrimForSurface(message.Body)
                }));
            }
            children.Add(new UiWidgetTree(UiKitVocabulary.List, new Dictionary<string, object?> { ["items"] = listItems }));
            children.Add(new UiWidgetTree(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Summarize last message",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = "summarize the last email",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }));
            children.Add(new UiWidgetTree(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Find related in Salesforce",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = "find salesforce accounts related to the last email",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }));
        }

        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(), children),
            [UiSurfaceKeys.Title] = "Gmail",
            [UiSurfaceKeys.SurfaceId] = "surface.gmail.recent",
            ["role"] = "assistant",
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task DeliverSalesforceRecordsSurfaceAsync(IReadOnlyList<string> records, string? clientId, string? workspaceId = null)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var children = new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = "Salesforce Accounts" })
        };

        if (records.Count == 0)
        {
            children.Add(new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "No Salesforce accounts were returned."
            }));
        }
        else
        {
            var listItems = new List<UiWidgetTree>();
            for (var i = 0; i < records.Count; i++)
            {
                listItems.Add(new UiWidgetTree(UiKitVocabulary.Tile, new Dictionary<string, object?>
                {
                    ["title"] = $"Account {i + 1}",
                    ["subtitle"] = TrimForSurface(records[i])
                }));
            }
            children.Add(new UiWidgetTree(UiKitVocabulary.List, new Dictionary<string, object?> { ["items"] = listItems }));
            children.Add(new UiWidgetTree(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Summarize last Salesforce",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = "summarize the last salesforce",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }));
        }

        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(), children),
            [UiSurfaceKeys.Title] = "Salesforce",
            [UiSurfaceKeys.SurfaceId] = "surface.salesforce.accounts",
            ["role"] = "assistant",
            ["workspaceId"] = workspaceId
        };
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task DeliverUiKitGallerySurfaceAsync(string? clientId, string? workspaceId = null)
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
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task DeliverLlmSettingsSurfaceAsync(string? clientId, string? workspaceId = null)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);

        string current = "default (Aspire composition or global IChatClient)";
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store != null)
        {
            try
            {
                var sys = await store.GetAsync("system", "llm");
                if (sys.TryGetValue("llm_provider", out var prov) && !string.IsNullOrWhiteSpace(prov))
                    current = prov;
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
                ["text"] = "Supported: ollama (e.g. qwen2.5-coder:1.5b), azureopenai (gpt-4o-mini), etc."
            }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "Click a button below to change (persisted to system/llm config and affects LlmResponder + Ino)."
            }),
            new(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Use Local Qwen (default dev)",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = "set-llm:qwen",
                ["clientId"] = clientId,
                ["workspaceId"] = workspaceId
            }),
            new(UiKitVocabulary.Button, new Dictionary<string, object?>
            {
                ["label"] = "Use Azure gpt-4o-mini",
                ["synapseType"] = nameof(InoRequest),
                ["prompt"] = "set-llm:gpt4o",
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
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task HandleLlmSetCommandAsync(InoRequest req, string workspaceId)
    {
        var p = req.Prompt.ToLowerInvariant();
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store == null)
        {
            await FireAsync(new InoResponse(req.Prompt, "No config store available to change LLM.", []));
            return;
        }

        string provider = "ollama";
        string key = "";
        if (p.Contains("gpt") || p.Contains("azure") || p.Contains("gpt4o"))
        {
            provider = "azureopenai";
        }
        else if (p.Contains("qwen") || p.Contains("local") || p.Contains("ollama"))
        {
            provider = "ollama";
        }
        // Support "set-llm:provider" syntax from buttons
        if (p.Contains("set-llm:"))
        {
            var idx = p.IndexOf("set-llm:") + 8;
            var val = p.Substring(idx).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (val.Contains("qwen") || val.Contains("ollama") || val == "local") provider = "ollama";
            else if (val.Contains("gpt") || val.Contains("azure")) provider = "azureopenai";
        }

        await store.SetAsync("system", "llm", new Dictionary<string, string> { ["llm_provider"] = provider, ["llm_key"] = key });

        await FireAsync(new InoResponse(req.Prompt, $"LLM provider set to {provider}.", []));
        await DeliverReplySurfaceAsync($"Active LLM updated to {provider} via system config. New requests will use it.", req.ClientId, workspaceId);

        // Refresh the settings surface so user sees the current value updated (feedback)
        await DeliverLlmSettingsSurfaceAsync(req.ClientId, workspaceId);
        await CreateMemorySummaryAsync(workspaceId);
    }

    private async Task HandleApproveProposalIntentAsync(InoRequest req, string workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var p = req.Prompt.ToLowerInvariant();

        // Extract proposal id from prompt e.g. "approve proposal automation-abc123" or "approve that one"
        string? proposalId = null;
        if (p.Contains("automation-"))
        {
            var idx = p.IndexOf("automation-");
            var candidate = p.Substring(idx).Split(' ', '\n', '\t', '.', ',', ':')[0].Trim();
            if (candidate.StartsWith("automation-")) proposalId = candidate;
        }
        else if (p.Contains("proposal "))
        {
            var parts = req.Prompt.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].ToLowerInvariant() == "proposal" && parts[i + 1].StartsWith("automation-", StringComparison.OrdinalIgnoreCase))
                {
                    proposalId = parts[i + 1];
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(proposalId))
        {
            // last pending automation proposal from journal as fallback
            var lastProposal = IncomingJournal.Concat(OutgoingJournal)
                .OfType<SelfEvolutionProposal>()
                .Where(pr => pr.ApplyVia == SelfEvolutionApplyVia.AutomationDefineReaction)
                .OrderByDescending(pr => pr.Timestamp)
                .FirstOrDefault();
            proposalId = lastProposal?.ProposalId;
        }

        if (string.IsNullOrWhiteSpace(proposalId))
        {
            await DeliverReplySurfaceAsync("No proposal id found to approve. Say 'approve proposal automation-xxxx'.", req.ClientId, workspaceId);
            return;
        }

        try
        {
            var approvalGrain = GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
            await approvalGrain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: "user-via-ino", Reason: "Approved from Ino chat"));
            await FireAsync(new InoResponse(req.Prompt, $"Approved proposal {proposalId}.", []));
            await DeliverReplySurfaceAsync($"Proposal {proposalId} approved. It will activate if the apply handler succeeds.", req.ClientId, workspaceId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to deliver approval decision for {Proposal}", proposalId);
            await DeliverReplySurfaceAsync($"Could not record approval for {proposalId}. Check self-evolution status.", req.ClientId, workspaceId);
        }
    }

    private async Task DeliverAutomationProposalSurfaceAsync(string proposalId, string rationale, string when, string script, string? clientId, string? workspaceId = null)
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
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task DeliverGraphSurfaceAsync(UiWidgetTree tree, string? clientId, string? workspaceId, string title, string surfaceId)
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
        if (clientId is not null) props["clientId"] = clientId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    public async Task<string> AskAsync(string prompt)
    {
        var result = await InteractAsync(new InoInteractRequest(prompt));
        return result.ResponseText;
    }

    public async Task<InoInteractResult> InteractAsync(InoInteractRequest request)
    {
        var clientId = request.ClientId;
        var workspaceId = WorkspaceIds.Effective(request.WorkspaceId);

        await FireAsync(new InoRequest(request.Prompt, clientId, workspaceId));

        // Allow handlers (classifier, LLM, surface delivery, proposal staging) to run.
        // In real use, journals are the source of truth; this is the contract collector.
        await Task.Delay(50);

        var tl = await GetOutgoingTimelineAsync();
        var response = tl.OfType<InoResponse>().LastOrDefault();

        // Intent
        var cls = InoIntentClassifier.Classify(request.Prompt);

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
                proposals = (await se.GetTimelineAsync())
                    .OfType<SelfEvolutionProposalPending>()
                    .TakeLast(request.MaxHistory)
                    .ToList();
            }
            catch { /* self-evo optional in some test setups */ }
        }

        // Available actions: derive from context + new architecture (automation proposals have Run/Approve)
        var actions = new List<InoAction>();
        if (request.IncludeActions)
        {
            // Generic follow-up
            actions.Add(new InoAction("Follow up with INO", FollowUpPrompt: "tell me more"));

            // If recent response or context suggests automation, surface the buttons
            var lastResp = response?.Response ?? "";
            if (lastResp.Contains("proposal", StringComparison.OrdinalIgnoreCase) ||
                lastResp.Contains("automation", StringComparison.OrdinalIgnoreCase) ||
                cls.Intent == "automation_create")
            {
                // These would normally come from the emitted surface; we synthesize for the contract
                actions.Add(new InoAction("Run now (preview)", FollowUpPrompt: "run automation latest"));
                actions.Add(new InoAction("Approve & activate", FollowUpPrompt: "approve proposal latest"));
            }

            if (cls.Intent == "uikit_gallery")
                actions.Add(new InoAction("Refresh gallery", FollowUpPrompt: "uikit gallery"));

            if (cls.Intent is "gmail" or "salesforce")
                actions.Add(new InoAction("Summarize last", FollowUpPrompt: "summarize the last one"));
        }

        return new InoInteractResult(
            Prompt: request.Prompt,
            ResponseText: response?.Response ?? "processed",
            ClassifiedIntent: cls.Intent,
            IntentConfidence: cls.Confidence,
            ClientId: clientId,
            WorkspaceId: workspaceId,
            UsedTaskIds: response?.UsedTaskIds ?? Array.Empty<string>(),
            RecentMemoryTopics: mems.Select(m => m.Topic).ToList(),
            AvailableActions: actions,
            PendingProposals: proposals,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    private async Task<string> BuildContextAsync(string prompt, string? workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var recentOut = OutgoingJournal.TakeLast(8).Select(s => s.Type + ":" + s.ToString()).ToList();
        var recentIn = IncomingJournal.TakeLast(5).Select(s => "in:" + s.ToString()).ToList();

        var completed = OutgoingJournal.OfType<TaskCompleted>().TakeLast(3);
        var taskCtx = string.Join(";", completed.Select(t => t.TaskId + "=" + (t.Result ?? "")));

        var mems = OutgoingJournal
            .OfType<MemorySummary>()
            .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId)
            .TakeLast(5);
        var memCtx = string.Join(";", mems.Select(m => m.Topic + "=" + m.Summary));

        // Include recently applied marketplace skills / installed packs so INO can use their code+desc at runtime.
        var skills = OutgoingJournal.Concat(IncomingJournal).OfType<SkillContextInjected>().TakeLast(2)
            .Select(s => s.SkillPackName + ":" + (s.Description.Length > 60 ? s.Description[..60] : s.Description));
        var packs = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().TakeLast(2)
            .Select(p => p.Pack.Name + "@" + p.Pack.Version);
        var skillCtx = string.Join(";", skills.Concat(packs));

        return $"prompt:{prompt}\nrecent-out:{string.Join(";", recentOut)}\nrecent-in:{string.Join(";", recentIn)}\ntasks:{taskCtx}\nmem:{memCtx}\nskills:{skillCtx}";
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
            return null;

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var clientMatch = schemas.LastOrDefault(schema => schema.ClientId == clientId);
            if (clientMatch is not null)
                return clientMatch;
        }

        return schemas[^1];
    }



    private static string GmailReplyText(IReadOnlyList<GmailMessageSummary> messages)
    {
        if (messages.Count == 0)
            return "No recent Gmail messages were returned.";

        var title = messages.Count == 1 ? "Latest Gmail message:" : "Recent Gmail messages:";
        var lines = messages.Select((m, i) => $"{i + 1}. {TrimForSurface(m.Body)}");
        return title + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string SalesforceReplyText(IReadOnlyList<string> records)
    {
        if (records.Count == 0)
            return "No Salesforce accounts were returned.";

        var title = records.Count == 1 ? "Latest Salesforce account:" : "Salesforce accounts:";
        var lines = records.Select((record, i) => $"{i + 1}. {TrimForSurface(record)}");
        return title + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string SalesforceFailureReply(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        if (message.StartsWith(SalesforceClientFactory.AuthenticationFailureMessage, StringComparison.Ordinal))
            return TrimForSurface(message);

        if (message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            return SalesforceClientFactory.AuthenticationFailureMessage;

        return "I couldn't query Salesforce: " + TrimForSurface(message) +
               ". Check your Salesforce credentials and try again.";
    }

    private static bool IsSalesforceIntegrationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName?.Contains("Salesforce", StringComparison.OrdinalIgnoreCase) == true ||
                current.Message.Contains("Salesforce", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    private async Task<string> ReasonWithLlmAsync(string prompt, string context)
    {
        var chat = await ResolveGlobalLlmClientAsync() ?? ServiceProvider.GetService<IChatClient>();
        if (chat == null) return $"[no-llm] INO would act on: {prompt} (ctx len {context.Length})";

        var sys = "You are INO, DigitalBrain's personal OS assistant. Use provided context from neuron journals. ALWAYS answer the user's request directly and visibly first with the actual content (e.g. the joke, summary, fact or help). Put any TASK: or BRANCH: directives ONLY on their own separate lines AFTER the answer, and ONLY if user explicitly asked to create a task/automation/branch. Never output only a directive. For a plain request like 'tell a joke' or 'generate a joke' just reply with the joke text directly.";
        var full = sys + "\nCTX:\n" + context + "\nUSER: " + prompt;
        var response = await chat.GetResponseAsync(full);
        return response.Text.Trim();
    }

    private async Task<string> ReasonDirectlyWithLlmAsync(string prompt, string context)
    {
        var chat = await ResolveGlobalLlmClientAsync() ?? ServiceProvider.GetService<IChatClient>();
        if (chat == null) return $"[no-llm] INO would act on: {prompt} (ctx len {context.Length})";

        var response = await chat.GetResponseAsync(
            "Answer the user's request directly in one or two sentences. Do not output TASK or BRANCH directives.\nCTX:\n"
            + context + "\nUSER: " + prompt);
        var text = response.Text.Trim();
        return string.IsNullOrWhiteSpace(text) ? "I do not have a useful answer yet." : text;
    }

    private async Task<IChatClient?> ResolveGlobalLlmClientAsync()
    {
        var factory = ServiceProvider.GetService<IScopedChatClientFactory>();
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (factory is null || store is null) return null;

        try
        {
            var sys = await store.GetAsync("system", "llm");
            if (sys.TryGetValue("llm_provider", out var provider) && !string.IsNullOrWhiteSpace(provider))
            {
                sys.TryGetValue("llm_key", out var key);
                return factory.Create(provider, string.IsNullOrWhiteSpace(key) ? null : key);
            }
        }
        catch { /* optional */ }
        return null;
    }

    private static ReplyPlan BuildReplyPlan(string prompt, string rawReply)
    {
        var visibleLines = new List<string>();
        var taskDescriptions = new List<string>();
        string? branchDescription = null;

        foreach (var line in rawReply.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("TASK:", StringComparison.OrdinalIgnoreCase))
            {
                var task = trimmed["TASK:".Length..].Trim();
                if (task.Length > 0)
                    taskDescriptions.Add(task);
                continue;
            }

            if (trimmed.StartsWith("BRANCH:", StringComparison.OrdinalIgnoreCase))
            {
                var branch = trimmed["BRANCH:".Length..].Trim();
                if (branch.Length > 0 && ShouldCreateBranch(prompt))
                    branchDescription = branch;
                continue;
            }

            if (line.Length > 0)
                visibleLines.Add(line);
        }

        var visible = string.Join(Environment.NewLine, visibleLines).Trim();
        // No longer synthesize "I'll start..." prefixes here — caller ensures direct visible answer via ReasonDirectly.
        // Directives are sidecar only; visible should be the answer or empty (then overridden).
        return new ReplyPlan(visible, taskDescriptions, branchDescription);
    }

    private static bool ShouldCreateBranch(string prompt) =>
        prompt.Contains("what if", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("branch", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("simulate", StringComparison.OrdinalIgnoreCase);

    private async Task<List<string>> OrchestrateActionsIfNeededAsync(ReplyPlan replyPlan)
    {
        var created = new List<string>();
        foreach (var taskDesc in replyPlan.TaskDescriptions)
        {
            var tid = "task-" + Guid.NewGuid().ToString("N")[..8];
            // Placeholder; full durable task orchestration via IKernelTask is coordinated from Kernel layer.
            created.Add(tid);
        }
        if (!string.IsNullOrWhiteSpace(replyPlan.BranchDescription))
        {
            var cp = await CreateCheckpointAsync();
            var bid = await BranchAsync(cp);
            created.Add("branch:" + bid.Value);
        }
        return created;
    }

    private async Task CreateMemorySummaryAsync(string? workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var recent = OutgoingJournal.Concat(IncomingJournal).TakeLast(20).ToList();
        if (recent.Count < 5) return;

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null) return;

        var ctx = string.Join("\n", recent.Select(s => s.Type + ": " + s.ToString()));
        var prompt = "Summarize the following recent activity in DigitalBrain for personal assistant memory. One short topic + 1-sentence summary. Activity:\n" + ctx;
        var response = await chat.GetResponseAsync(prompt);
        var summaryText = response.Text.Trim();
        if (summaryText.Length > 10)
        {
            var topic = summaryText.Split('.')[0].Trim();
            var mem = new MemorySummary(topic.Length > 30 ? topic.Substring(0, 30) : topic, summaryText, DateTimeOffset.UtcNow, workspaceId);
            await FireAsync(mem);
        }
    }

    private string? GetLastGmailBodiesFromJournal(string? workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var recentMem = IncomingJournal.Concat(OutgoingJournal)
            .OfType<MemorySummary>()
            .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId &&
                        !string.IsNullOrWhiteSpace(m.Summary) &&
                        (string.IsNullOrWhiteSpace(m.Topic) ||
                         m.Topic.ToLowerInvariant().Contains("gmail") ||
                         m.Topic.ToLowerInvariant().Contains("email") ||
                         m.Topic.ToLowerInvariant().Contains("last-gmail") ||
                         m.Topic.ToLowerInvariant().Contains("mail")))
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefault();
        if (recentMem != null) return recentMem.Summary;

        // Explicitly support lookup last GmailMessagesReady (per plan) + associated context
        var lastGmailReady = IncomingJournal.Concat(OutgoingJournal)
            .OfType<Signal>()
            .Where(s => s.Name == GoogleSignals.GmailMessagesReady &&
                        WorkspaceIds.Effective(s.Props.TryGetValue("workspaceId", out var w) ? w?.ToString() : null) == workspaceId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefault();
        if (lastGmailReady != null)
        {
            // Prefer a nearby or recent gmail mem; else synthesize note from signal
            var nearbyMem = IncomingJournal.Concat(OutgoingJournal)
                .OfType<MemorySummary>()
                .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId && !string.IsNullOrWhiteSpace(m.Summary) &&
                            (m.Topic?.ToLowerInvariant().Contains("gmail") == true || m.Topic?.ToLowerInvariant().Contains("email") == true))
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();
            if (nearbyMem != null) return nearbyMem.Summary;

            var count = lastGmailReady.Props.TryGetValue("count", out var c) ? c?.ToString() : "?";
            return $"[from GmailMessagesReady signal] {count} messages fetched recently (ids: {lastGmailReady.Props.GetValueOrDefault("messageIds")}). Use prior journal summaries for body details.";
        }

        // Cross-turn: if last relevant request was gmail-ish, return most recent memory summary as fallback context
        var lastRelevantReq = IncomingJournal.OfType<InoRequest>()
            .LastOrDefault(r => InoConnectorIntents.IsGmail(r.Prompt) || InoConnectorIntents.IsSalesforce(r.Prompt));
        if (lastRelevantReq != null)
        {
            var lastAnyMem = IncomingJournal.Concat(OutgoingJournal)
                .OfType<MemorySummary>()
                .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId && !string.IsNullOrWhiteSpace(m.Summary))
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();
            return lastAnyMem?.Summary;
        }
        return null;
    }

    private string? GetLastSalesforceFromJournal(string? workspaceId)
    {
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var recentMem = IncomingJournal.Concat(OutgoingJournal)
            .OfType<MemorySummary>()
            .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId &&
                        !string.IsNullOrWhiteSpace(m.Summary) &&
                        (string.IsNullOrWhiteSpace(m.Topic) ||
                         m.Topic.ToLowerInvariant().Contains("salesforce") ||
                         m.Topic.ToLowerInvariant().Contains("crm") ||
                         m.Topic.ToLowerInvariant().Contains("last-salesforce") ||
                         m.Topic.ToLowerInvariant().Contains("account")))
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefault();
        if (recentMem != null) return recentMem.Summary;

        // Cross-turn fallback using most recent mem for workspace if prior SF request seen
        var lastSfReq = IncomingJournal.OfType<InoRequest>()
            .LastOrDefault(r => InoConnectorIntents.IsSalesforce(r.Prompt));
        if (lastSfReq != null)
        {
            var lastAnyMem = IncomingJournal.Concat(OutgoingJournal)
                .OfType<MemorySummary>()
                .Where(m => WorkspaceIds.Effective(m.WorkspaceId) == workspaceId && !string.IsNullOrWhiteSpace(m.Summary))
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();
            return lastAnyMem?.Summary;
        }
        return null;
    }
}

