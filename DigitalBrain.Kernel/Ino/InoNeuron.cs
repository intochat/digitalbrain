using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Ui;
using DigitalBrain.Core.UiKit;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Market;
using DigitalBrain.UiKit;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Ino;

// INO: ultra-context personal assistant neuron.
// Uses dual journals as primary memory (recent + full history), spawns KernelTasks for actions,
// can drive checkpoints/branches for planning. Context is multi-scale via recency + LLM summary.
[GrainType("ino.personal.v1")]
public class InoNeuron(ILogger<InoNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IInoNeuron
{
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

        var ctx = await BuildContextAsync(req.Prompt);
        var reply = await ReasonWithLlmAsync(req.Prompt, ctx);

        var taskIds = await OrchestrateActionsIfNeededAsync(req.Prompt, reply);

        await FireAsync(new InoResponse(req.Prompt, reply, taskIds.ToArray()));
        await DeliverReplySurfaceAsync(reply, req.SessionId);

        // Compress recent activity to long-term memory summary (journal driven).
        await CreateMemorySummaryAsync();
    }

    private static bool IsBitcoinPriceIntent(string prompt) =>
        prompt.Contains("bitcoin", StringComparison.OrdinalIgnoreCase) &&
        prompt.Contains("price", StringComparison.OrdinalIgnoreCase);

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

    private async Task<string> ReasonWithLlmAsync(string prompt, string context)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null) return $"[no-llm] INO would act on: {prompt} (ctx len {context.Length})";

        var sys = "You are INO, DigitalBrain's personal OS assistant. Use provided context from neuron journals. Be concise, propose kernel tasks or branches when useful. Output action if any as 'TASK: desc' or 'BRANCH: whatif'.";
        var full = sys + "\nCTX:\n" + context + "\nUSER: " + prompt;
        var response = await chat.GetResponseAsync(full);
        return response.Text.Trim();
    }

    private async Task<List<string>> OrchestrateActionsIfNeededAsync(string prompt, string reply)
    {
        var created = new List<string>();
        if (reply.Contains("TASK:", StringComparison.OrdinalIgnoreCase))
        {
            var taskDesc = reply.Split("TASK:", 2)[1].Split('\n')[0].Trim();
            var tid = "task-" + Guid.NewGuid().ToString("N")[..8];
            var kt = GrainFactory.GetGrain<IKernelTask>(tid);
            await kt.FireAsync(new RunTask(tid, taskDesc));
            created.Add(tid);
        }
        if (reply.Contains("BRANCH:", StringComparison.OrdinalIgnoreCase) || prompt.Contains("what if", StringComparison.OrdinalIgnoreCase))
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

