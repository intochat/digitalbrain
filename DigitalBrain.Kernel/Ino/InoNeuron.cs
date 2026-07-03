using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Ui;
using DigitalBrain.Core.UiKit;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Market;
using DigitalBrain.UiKit;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Ino;

// INO: ultra-context personal assistant neuron.
// Uses dual journals as primary memory (recent + full history), spawns KernelTasks for actions,
// can drive checkpoints/branches for planning. Context is multi-scale via recency + LLM summary.
[GrainType("ino.personal.v1")]
public class InoNeuron(ILogger<InoNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IInoNeuron, IHandle<Signal>
{
    private sealed record ReplyPlan(string VisibleReply, IReadOnlyList<string> TaskDescriptions, string? BranchDescription);
    private sealed record GmailMessageSummary(string Id, string Body);

    private InoRequest? _pendingGmailRequest;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
    }

    public async Task HandleAsync(InoRequest req)
    {
        if (IsBitcoinPriceIntent(req.Prompt))
        {
            var price = await GrainFactory.GetGrain<IMarketDataNeuron>("market-data-main").GetBitcoinPriceUsdAsync();
            var priceReply = $"The current Bitcoin price is {price}.";
            await FireAsync(new InoResponse(req.Prompt, priceReply, []));
            await DeliverReplySurfaceAsync(priceReply, req.SessionId);
            return;
        }

        if (IsTwoObjectRelationIntent(req.Prompt))
        {
            await FireAsync(new InoResponse(req.Prompt, "Rendered a relation graph.", []));
            await DeliverGraphSurfaceAsync(
                DbSchemaGraphMapper.RelationOfTwoObjectsTree(),
                req.SessionId,
                "Object relation",
                "surface.graph.relation");
            return;
        }

        if (IsSchemaVisualizationIntent(req.Prompt))
        {
            if (TryExtractDatabasePath(req.Prompt, out var databasePath))
            {
                var inspected = await InspectReferencedDatabaseAsync(databasePath, req.SessionId);
                if (inspected is not null)
                {
                    await FireAsync(new InoResponse(req.Prompt, SchemaReplyText(inspected), []));
                    await FireAsync(inspected);
                    return;
                }
            }

            var latest = LatestSuccessfulSchema(req.SessionId);
            if (latest?.Schema is not null)
            {
                await FireAsync(new InoResponse(req.Prompt, "Rendered the most recent database schema.", []));
                await ProcessSchemaInspectedAsync(latest, req.SessionId ?? latest.SessionId);
                return;
            }
        }

        if (IsGmailIntent(req.Prompt))
        {
            await HandleGmailIntentAsync(req);
            return;
        }

        var ctx = await BuildContextAsync(req.Prompt);
        var rawReply = await ReasonWithLlmAsync(req.Prompt, ctx);
        var replyPlan = BuildReplyPlan(req.Prompt, rawReply);
        if (string.IsNullOrWhiteSpace(replyPlan.VisibleReply))
        {
            var directReply = await ReasonDirectlyWithLlmAsync(req.Prompt, ctx);
            replyPlan = replyPlan with { VisibleReply = directReply };
        }

        var taskIds = await OrchestrateActionsIfNeededAsync(replyPlan);

        await FireAsync(new InoResponse(req.Prompt, replyPlan.VisibleReply, taskIds.ToArray()));
        await DeliverReplySurfaceAsync(replyPlan.VisibleReply, req.SessionId);

        // Compress recent activity to long-term memory summary (journal driven).
        await CreateMemorySummaryAsync();
    }

    private static bool IsBitcoinPriceIntent(string prompt) =>
        prompt.Contains("bitcoin", StringComparison.OrdinalIgnoreCase) &&
        prompt.Contains("price", StringComparison.OrdinalIgnoreCase);

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != GoogleSignals.AuthCompleted)
            return;

        var pending = _pendingGmailRequest
            ?? IncomingJournal.OfType<InoRequest>().LastOrDefault(r => IsGmailIntent(r.Prompt));

        if (pending is null || !await HasGoogleCredentialAsync())
            return;

        _pendingGmailRequest = null;
        await FetchRecentGmailAsync(pending);
    }

    public async Task HandleAsync(TabularDataIngested ingested)
    {
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
        if (ingested.SessionId is not null) props["sessionId"] = ingested.SessionId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));

        // Deterministic (not LLM-generated) so follow-up questions can find this data via BuildContextAsync
        // even when no IChatClient is configured (the [no-llm] fallback path).
        var summary = $"Uploaded '{ingested.FileName}' with columns [{string.Join(", ", headers)}] and {rows.Count} data rows. Column stats: {ingested.ColumnStatsJson}";
        await FireAsync(new MemorySummary(ingested.FileName, summary, DateTimeOffset.UtcNow));
    }

    public Task HandleAsync(DbSchemaInspected inspected) =>
        ProcessSchemaInspectedAsync(inspected, inspected.SessionId);

    private async Task ProcessSchemaInspectedAsync(DbSchemaInspected inspected, string? sessionId)
    {
        if (!inspected.Succeeded || inspected.Schema is null)
        {
            var message = $"I could not inspect database schema '{inspected.ConnectionName}': {inspected.Error ?? "unknown error"}.";
            await DeliverReplySurfaceAsync(message, sessionId);
            return;
        }

        var schema = inspected.Schema with { SessionId = sessionId ?? inspected.Schema.SessionId };
        await DeliverGraphSurfaceAsync(
            DbSchemaGraphMapper.ToGraphCanvasTree(schema),
            sessionId ?? schema.SessionId,
            $"{schema.ConnectionName} schema",
            "surface.db-schema." + StableSurfaceId(schema.ConnectionName));

        await FireAsync(new MemorySummary(
            schema.ConnectionName,
            SchemaMemorySummary(schema),
            DateTimeOffset.UtcNow));
    }

    private async Task<DbSchemaInspected?> InspectReferencedDatabaseAsync(string databasePath, string? sessionId)
    {
        var connectionName = Path.GetFileNameWithoutExtension(databasePath);
        if (string.IsNullOrWhiteSpace(connectionName))
            connectionName = "sqlite-db";

        var cmd = new DbInspectSchema(connectionName, "sqlite", SourcePath: databasePath, SessionId: sessionId);
        var db = GrainFactory.GetGrain<IDbSupportNeuron>("db-main");
        await db.FireAsync(cmd);

        var timeline = await db.GetTimelineAsync();
        return timeline
            .OfType<DbSchemaInspected>()
            .LastOrDefault(result => result.CorrelationId == cmd.SynapseId)
            ?? timeline.OfType<DbSchemaInspected>().LastOrDefault(result => result.ConnectionName == connectionName);
    }

    private async Task DeliverReplySurfaceAsync(string reply, string? sessionId)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = reply }),
            [UiSurfaceKeys.Title] = "INO",
            ["role"] = "assistant",
        };
        if (sessionId is not null) props["sessionId"] = sessionId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task HandleGmailIntentAsync(InoRequest req)
    {
        if (!await HasGoogleCredentialAsync())
        {
            _pendingGmailRequest = req;
            var reply = "Google authentication is required to read Gmail.";
            await FireAsync(new InoResponse(req.Prompt, reply, []));
            await DeliverGoogleAuthSurfaceAsync(req.SessionId);
            return;
        }

        await FetchRecentGmailAsync(req);
    }

    private async Task<bool> HasGoogleCredentialAsync()
    {
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (store is null)
            return false;

        try
        {
            var values = await store.GetAsync("default", "google");
            return HasValue(values, "client_id") &&
                   HasValue(values, "client_secret") &&
                   HasValue(values, "refresh_token");
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Google credential check failed.");
            return false;
        }

        static bool HasValue(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    private async Task DeliverGoogleAuthSurfaceAsync(string? sessionId)
    {
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
        };
        if (sessionId is not null) props["sessionId"] = sessionId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task FetchRecentGmailAsync(InoRequest req)
    {
        var maxResults = GmailResultCount(req.Prompt);
        await Broadcast(new Signal(GoogleSignals.GmailFetchRequested, new Dictionary<string, object?>
        {
            ["prompt"] = req.Prompt,
            ["sessionId"] = req.SessionId,
            ["maxResults"] = maxResults
        }));

        var gmail = GrainFactory.GetGrain<IGmailNeuron>("gmail-main");
        var ids = await gmail.ListMessagesAsync("", maxResults);
        var summaries = new List<GmailMessageSummary>();

        foreach (var id in ids.Take(maxResults))
        {
            var body = await gmail.ReadMessageAsync(id);
            summaries.Add(new GmailMessageSummary(id, body));
        }

        await Broadcast(new Signal(GoogleSignals.GmailMessagesReady, new Dictionary<string, object?>
        {
            ["sessionId"] = req.SessionId,
            ["count"] = summaries.Count,
            ["messageIds"] = string.Join(",", summaries.Select(m => m.Id))
        }));

        var reply = GmailReplyText(summaries);
        await FireAsync(new InoResponse(req.Prompt, reply, []));
        await DeliverGmailMessagesSurfaceAsync(summaries, req.SessionId);
    }

    private async Task DeliverGmailMessagesSurfaceAsync(IReadOnlyList<GmailMessageSummary> messages, string? sessionId)
    {
        var children = new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = "Recent Gmail" })
        };

        if (messages.Count == 0)
        {
            children.Add(new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?>
            {
                ["text"] = "No recent Gmail messages were returned."
            }));
        }
        else
        {
            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                children.Add(new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?>
                {
                    ["text"] = $"{i + 1}. {TrimForSurface(message.Body)}"
                }));
            }
        }

        var props = new Dictionary<string, object?>
        {
            ["tree"] = new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(), children),
            [UiSurfaceKeys.Title] = "Gmail",
            [UiSurfaceKeys.SurfaceId] = "surface.gmail.recent",
            ["role"] = "assistant",
        };
        if (sessionId is not null) props["sessionId"] = sessionId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    private async Task DeliverGraphSurfaceAsync(UiWidgetTree tree, string? sessionId, string title, string surfaceId)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.Title] = title,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
            ["role"] = "assistant",
            ["surfaceKind"] = UiSurfaceKinds.GraphCanvas,
        };
        if (sessionId is not null) props["sessionId"] = sessionId;

        var surface = new UiSurface(UiSurface.WidgetTreeKind, props);
        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(StampCurrent(surface));
    }

    public async Task<string> AskAsync(string prompt)
    {
        await FireAsync(new InoRequest(prompt));
        var tl = await GetOutgoingTimelineAsync();
        var last = tl.OfType<InoResponse>().LastOrDefault();
        return last?.Response ?? "processed";
    }

    private async Task<string> BuildContextAsync(string prompt)
    {
        var recentOut = OutgoingJournal.TakeLast(8).Select(s => s.Type + ":" + s.ToString()).ToList();
        var recentIn = IncomingJournal.TakeLast(5).Select(s => "in:" + s.ToString()).ToList();

        var completed = OutgoingJournal.OfType<TaskCompleted>().TakeLast(3);
        var taskCtx = string.Join(";", completed.Select(t => t.TaskId + "=" + (t.Result ?? "")));

        var mems = OutgoingJournal.OfType<MemorySummary>().TakeLast(5);
        var memCtx = string.Join(";", mems.Select(m => m.Topic + "=" + m.Summary));

        // Include recently applied marketplace skills / installed packs so INO can use their code+desc at runtime.
        var skills = OutgoingJournal.Concat(IncomingJournal).OfType<SkillContextInjected>().TakeLast(2)
            .Select(s => s.SkillPackName + ":" + (s.Description.Length > 60 ? s.Description[..60] : s.Description));
        var packs = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().TakeLast(2)
            .Select(p => p.Pack.Name + "@" + p.Pack.Version);
        var skillCtx = string.Join(";", skills.Concat(packs));

        // Recent editor activity for INO awareness of live edits.
        var edits = OutgoingJournal.Concat(IncomingJournal).OfType<InoCodeEdit>().TakeLast(1).Select(e => "edit:" + (e.Code.Length > 80 ? e.Code[..80] : e.Code));
        var editorCtx = string.Join(";", edits);

        return $"prompt:{prompt}\nrecent-out:{string.Join(";", recentOut)}\nrecent-in:{string.Join(";", recentIn)}\ntasks:{taskCtx}\nmem:{memCtx}\nskills:{skillCtx}\neditor:{editorCtx}";
    }

    private DbSchemaInspected? LatestSuccessfulSchema(string? sessionId)
    {
        var schemas = IncomingJournal
            .Concat(OutgoingJournal)
            .OfType<DbSchemaInspected>()
            .Where(schema => schema.Succeeded && schema.Schema is not null)
            .DistinctBy(schema => schema.SynapseId)
            .OrderBy(schema => schema.Timestamp)
            .ToList();

        if (schemas.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var sessionMatch = schemas.LastOrDefault(schema => schema.SessionId == sessionId);
            if (sessionMatch is not null)
                return sessionMatch;
        }

        return schemas[^1];
    }

    private static bool IsSchemaVisualizationIntent(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("schema") ||
               p.Contains("visualize database") ||
               p.Contains("visualize db") ||
               p.Contains("show database") ||
               p.Contains("show db");
    }

    private static bool IsGmailIntent(string prompt) =>
        GmailIntentRegex().IsMatch(prompt);

    private static int GmailResultCount(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("last") || p.Contains("latest") || p.Contains("most recent") ? 1 : 5;
    }

    private static string GmailReplyText(IReadOnlyList<GmailMessageSummary> messages)
    {
        if (messages.Count == 0)
            return "No recent Gmail messages were returned.";

        var title = messages.Count == 1 ? "Latest Gmail message:" : "Recent Gmail messages:";
        var lines = messages.Select((m, i) => $"{i + 1}. {TrimForSurface(m.Body)}");
        return title + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string TrimForSurface(string value)
    {
        var text = Regex.Replace(value.Trim(), @"\s+", " ");
        return text.Length <= 280 ? text : text[..277] + "...";
    }

    private static bool IsTwoObjectRelationIntent(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return (p.Contains("draw") || p.Contains("show") || p.Contains("visualize")) &&
               p.Contains("relation") &&
               (p.Contains("2 objects") || p.Contains("two objects") || p.Contains("object"));
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

    private static Regex GmailIntentRegex() =>
        new(@"\b(gmail|email|e-mail|mailbox|inbox)\b",
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
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null) return $"[no-llm] INO would act on: {prompt} (ctx len {context.Length})";

        var sys = "You are INO, DigitalBrain's personal OS assistant. Use provided context from neuron journals. Be concise and answer the user's request directly. Only add action directives on separate lines when the user explicitly asks to create a task, branch, simulation, or what-if. Valid directives are 'TASK: desc' and 'BRANCH: whatif'. Never let a directive replace the user-visible answer.";
        var full = sys + "\nCTX:\n" + context + "\nUSER: " + prompt;
        var response = await chat.GetResponseAsync(full);
        return response.Text.Trim();
    }

    private async Task<string> ReasonDirectlyWithLlmAsync(string prompt, string context)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null) return $"[no-llm] INO would act on: {prompt} (ctx len {context.Length})";

        var response = await chat.GetResponseAsync(
            "Answer the user's request directly in one or two sentences. Do not output TASK or BRANCH directives.\nCTX:\n"
            + context + "\nUSER: " + prompt);
        var text = response.Text.Trim();
        return string.IsNullOrWhiteSpace(text) ? "I do not have a useful answer yet." : text;
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
        if (string.IsNullOrWhiteSpace(visible))
        {
            if (taskDescriptions.Count > 0)
                visible = "I'll start that task: " + taskDescriptions[0];
            else if (!string.IsNullOrWhiteSpace(branchDescription))
                visible = "I'll open a branch to explore: " + branchDescription;
        }

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
            var kt = GrainFactory.GetGrain<IKernelTask>(tid);
            await kt.FireAsync(new RunTask(tid, taskDesc));
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

    private async Task CreateMemorySummaryAsync()
    {
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
            var mem = new MemorySummary(topic.Length > 30 ? topic.Substring(0,30) : topic, summaryText, DateTimeOffset.UtcNow);
            await FireAsync(mem);
        }
    }
}

