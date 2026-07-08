using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.AI;
using Microsoft.CodeAnalysis;
using DigitalBrain.Core.Distribution;
using DigitalBrain.Kernel.Ui;
namespace DigitalBrain.Kernel;

using DigitalBrain.Pack.Contracts;
using DigitalBrain.Ui.Contracts;
using DigitalBrain.Ui.Contracts.Ui;


[GrainType("digitalbrain.generated")]
public class GeneratedNeuron(ILogger<GeneratedNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IGeneratedNeuron, IHandle<NeuronTelemetry>
{
    private GeneratedPackRuntime? _packRuntime;

    private GeneratedPackRuntime PackRuntime => _packRuntime ??= new GeneratedPackRuntime(ServiceProvider, Logger);

    public Task HandleAsync(NeuronTelemetry telemetry, CancellationToken cancellationToken = default) => Task.CompletedTask;

    protected override bool ShouldSubscribeToTimeline => true;

    public override async Task OnNextAsync(Synapse item, Orleans.Streams.StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);

        EnsureEmbodied();
        if (await TryDispatchEmbodiedAsync(item))
            return;

        await DispatchBroadcastIfHandledAsync(item);
    }

    public override Task OnDeactivateAsync(Orleans.DeactivationReason reason, CancellationToken cancellationToken)
    {
        _packRuntime?.Dispose();
        _packRuntime = null;
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    protected override async Task DispatchSynapse(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = this.GetPrimaryKeyString() ?? "unknown-generated";
        Logger.LogInformation("GeneratedNeuron {Id} dispatched {Type}", id, synapse.Type);
        await FireAsync(new NeuronTelemetry(Self, "generated-dispatched"), cancellationToken);

        switch (synapse)
        {
            case NeuroPackInstalled installed:
                PackRuntime.Install(installed.Pack);
                await EmitConfigFormIfRequiredAsync(cancellationToken);
                return;
        }

        if (await TryDispatchEmbodiedAsync(synapse, cancellationToken))
        {
            return;
        }

        switch (synapse)
        {
            case Signal sig when sig.Name == "DemoMessage":
                Logger.LogInformation("Generated handled demo message");
                break;
            case ExperienceUsed used:
                await UseExperienceAsync(used, cancellationToken);
                break;
        }
    }

    private async Task EmitConfigFormIfRequiredAsync(CancellationToken cancellationToken)
    {
        var embodied = PackRuntime.Current;
        if (embodied is null) return;

        var required = embodied.GetManifest().RequiredConfig;
        if (required is null || required.Count == 0) return;

        var surface = ConfigFormSurface.Build(embodied.PackName, required, Self.Value);
        await FireAsync(surface, cancellationToken);
        if (ServiceProvider.GetService<HomeFeedBus>() is { } bus)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value), cancellationToken);
        }
        Logger.LogInformation("GeneratedNeuron emitted config form for pack '{Pack}' ({FieldCount} fields).", embodied.PackName, required.Count);
    }

    private void EnsureEmbodied() =>
        PackRuntime.Ensure(OutgoingJournal.Concat(IncomingJournal), this.GetPrimaryKeyString() ?? string.Empty);

    private async Task<bool> TryDispatchEmbodiedAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEmbodied();
        var embodied = PackRuntime.Current;
        if (embodied is null || !embodied.CanHandle(synapse))
        {
            return false;
        }

        var manifest = embodied.GetManifest();
        IReadOnlyList<Synapse> outputs;
        try
        {
            outputs = embodied.Handle(synapse);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Embodied pack '{Pack}' failed while handling {SynapseType}.", embodied.PackName, synapse.Type);
            await FireAsync(new PackEmission(embodied.PackName, synapse.Type, "pack-error:" + ex.GetBaseException().Message), cancellationToken);
            return true;
        }

        foreach (var output in outputs)
        {
            var normalized = NormalizePackOutput(embodied.PackName, output);
            await Broadcast(normalized, cancellationToken);
            await BroadcastPackSurfaceAsync(normalized, embodied.PackName, cancellationToken);
        }

        Logger.LogInformation(
            "GeneratedNeuron dispatched {SynapseType} to embodied pack '{Pack}' (manifest: {ManifestTypes}) and emitted {Count} synapse(s).",
            synapse.Type,
            embodied.PackName,
            string.Join(',', manifest.HandledSynapseTypes.Select(t => t.Value)),
            outputs.Count);
        return true;
    }

    private static Synapse NormalizePackOutput(string packName, Synapse output)
    {
        var normalized = output is PackEmission emission
            ? emission with { Pack = packName }
            : output;

        return normalized with
        {
            CorrelationId = null,
            CausationId = null,
            SynapseId = Guid.NewGuid().ToString("N")
        };
    }

    private async Task BroadcastPackSurfaceAsync(Synapse output, string packName, CancellationToken cancellationToken)
    {
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is null) return;

        if (output is UiSurface surface)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, packName), cancellationToken);
        }
        else if (output is RfwCard card)
        {
            await bus.BroadcastAsync(card, cancellationToken);
        }
    }

    private async Task UseExperienceAsync(ExperienceUsed used, CancellationToken cancellationToken)
    {
        if (IsGmailInsightsExperience(used))
        {
            await RunGmailInsightsExperienceAsync(used, cancellationToken);
            return;
        }

        EnsureEmbodied();

        var embodied = PackRuntime.Current;
        if (embodied is not null)
        {
            var output = embodied.Respond(used.Action);
            await FireAsync(new PackEmission(embodied.PackName, used.Action, output), cancellationToken);
            Logger.LogInformation("GeneratedNeuron ran embodied pack '{Pack}' for action '{Action}'", embodied.PackName, used.Action);
            if (used.Action is "open" or "emit-test-surface" or "self-test")
            {
                var winTree = new UiWidgetTree("fcard", new Dictionary<string, object?> { ["title"] = used.Pack + " - " + used.Action }, new List<UiWidgetTree> { new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "Live from embodied " + used.Pack }) });
                var surf = new UiSurface(used.Pack, new Dictionary<string, object?> { [UiSurfaceKeys.Title] = used.Pack, ["pack"] = used.Pack, ["tree"] = winTree });
                await FireAsync(surf, cancellationToken);
                var b = ServiceProvider.GetService<HomeFeedBus>();
                if (b is not null)
                {
                    await b.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surf, Self.Value), cancellationToken);
                }
            }
        }

        var inst = LastInstalledPack();
        if (inst is null)
        {
            Logger.LogInformation("Generated experience {Pack} used: {Action} (no installed pack yet).", used.Pack, used.Action);
            return;
        }

        var (packKey, code, desc) = inst.Value;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            await FireAsync(new LlmResponse(used.Pack, $"[Embodied: {packKey}] Simulated response to {used.Action} using installed experience.", "sim"), cancellationToken);
        }
        else
        {
            var behaviorPrompt = $"You are now the installed experience '{packKey}'.\n" +
                                 $"Description: {desc}\n" +
                                 $"Implementation guidance/code:\n{code}\n\n" +
                                 $"Handle the following usage: {used.Action} on input related to '{used.Pack}'.\n" +
                                 "Respond in character as this specific installed neuron/experience would. Be concise and useful.";
            var response = await chat.GetResponseAsync(behaviorPrompt, cancellationToken: cancellationToken);
            await FireAsync(new LlmResponse(behaviorPrompt, response.Text.Trim(), "embodied-pack"), cancellationToken);
            Logger.LogInformation("GeneratedNeuron LLM-embodied pack '{Pack}' for action '{Action}'", packKey, used.Action);
        }

        if (used.Action is "open" or "emit-test-surface" or "self-test")
        {
            var winTree = new UiWidgetTree("fcard", new Dictionary<string, object?> { ["title"] = used.Pack + " - " + used.Action }, new List<UiWidgetTree> { new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "Live surface from " + used.Pack + " pack scenario." }) });
            var surf = new UiSurface(used.Pack, new Dictionary<string, object?>
            {
                [UiSurfaceKeys.Title] = used.Pack,
                ["pack"] = used.Pack,
                ["tree"] = winTree
            });
            await FireAsync(surf, cancellationToken);
            var bus = ServiceProvider.GetService<HomeFeedBus>();
            if (bus != null)
            {
                await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surf, Self.Value), cancellationToken);
            }
        }
    }

    private async Task RunGmailInsightsExperienceAsync(ExperienceUsed used, CancellationToken cancellationToken)
    {
        var userId = EffectiveUserId(used.UserId);
        var emails = BuildGmailSampleRows(100);
        var categoryRows = emails
            .GroupBy(row => row["category"]?.ToString() ?? "Other", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => new Dictionary<string, object?>
            {
                ["category"] = group.Key,
                ["count"] = group.Count()
            })
            .ToArray();

        var summary = await SummarizeGmailRowsAsync(emails, cancellationToken);
        var chartRequestId = "gmail-last-100-" + StableKey(userId);
        await FireAsync(new PackEmission(used.Pack, used.Action, summary), cancellationToken);

        var surface = BuildGmailInsightsSurface(used, summary, emails.Count, chartRequestId);
        await FireAsync(surface, cancellationToken);
        if (ServiceProvider.GetService<HomeFeedBus>() is { } bus)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value), cancellationToken);
        }

        var chart = GrainFactory.GetGrain<IDataVisualizationNeuron>("chart-" + chartRequestId);
        await chart.FireAsync(new VisualizeDataRequest(
            "Gmail last 100 emails by category",
            System.Text.Json.JsonSerializer.Serialize(categoryRows),
            "bar",
            chartRequestId,
            userId,
            used.SessionId), cancellationToken);
    }

    private async Task<string> SummarizeGmailRowsAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> emails, CancellationToken cancellationToken)
    {
        var fallback = $"Local Gmail Insights analyzed {emails.Count} messages. Top categories: " +
            string.Join(", ", emails
                .GroupBy(row => row["category"]?.ToString() ?? "Other", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Take(3)
                .Select(group => group.Key + " " + group.Count()));

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            return fallback;
        }

        var sample = string.Join("\n", emails.Take(20).Select(row =>
            "- " + row["from"] + " | " + row["subject"] + " | " + row["category"]));
        try
        {
            var response = await chat.GetResponseAsync(
                "You are the local DigitalBrain Gmail insights experience. " +
                "Summarize these recent Gmail messages in two concise bullets and name the dominant categories.\n" +
                sample,
                cancellationToken: cancellationToken);
            var text = response.Text.Trim();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Local LLM Gmail summary failed; using deterministic fallback.");
            return fallback;
        }
    }

    private UiSurface BuildGmailInsightsSurface(ExperienceUsed used, string summary, int emailCount, string chartRequestId)
    {
        var tree = new UiWidgetTree(
            "fcard",
            new Dictionary<string, object?>
            {
                ["title"] = "Gmail Insights",
                ["subtitle"] = emailCount + " messages analyzed locally"
            },
            new List<UiWidgetTree>
            {
                new("text", new Dictionary<string, object?> { ["text"] = summary }),
                new("text", new Dictionary<string, object?> { ["text"] = "Chart request: " + chartRequestId })
            });

        return new UiSurface("gmail-insights", new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "surface.gmail-insights." + chartRequestId,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Gmail Insights",
            [UiSurfaceKeys.Priority] = 30,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["pack"] = used.Pack,
            ["action"] = used.Action,
            ["userId"] = EffectiveUserId(used.UserId),
            ["sessionId"] = used.SessionId,
            ["emailCount"] = emailCount,
            ["summary"] = summary,
            ["chartRequestId"] = chartRequestId,
            ["source"] = "local-sample",
            ["tree"] = tree
        });
    }

    private static bool IsGmailInsightsExperience(ExperienceUsed used) =>
        used.Pack.Equals("DigitalBrain.Experience.GmailInsights", StringComparison.OrdinalIgnoreCase) ||
        used.Action.StartsWith("gmail:", StringComparison.OrdinalIgnoreCase);

    private static string EffectiveUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();

    private static string StableKey(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        var key = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "anonymous" : key;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> BuildGmailSampleRows(int count)
    {
        string[] senders =
        [
            "alerts@github.com",
            "billing@cloud.local",
            "calendar@google.com",
            "team@digitalbrain.local",
            "newsletter@aiweekly.example",
            "support@customer.example",
            "security@accounts.google.com",
            "noreply@stripe.com"
        ];
        string[] categories = ["Engineering", "Billing", "Calendar", "Team", "Newsletter", "Support", "Security", "Payments"];
        string[] subjects =
        [
            "Build completed for kernel runtime",
            "Invoice available for review",
            "Meeting moved to tomorrow",
            "Product surface review notes",
            "Local AI tooling digest",
            "Customer follow-up requested",
            "Security alert for account access",
            "Payment receipt"
        ];

        var now = DateTimeOffset.UtcNow;
        var rows = new List<IReadOnlyDictionary<string, object?>>(count);
        for (var i = 0; i < count; i++)
        {
            var ix = i % categories.Length;
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = "gmail-local-" + (i + 1).ToString("000"),
                ["receivedAt"] = now.AddMinutes(-37 * i).ToString("O"),
                ["from"] = senders[ix],
                ["subject"] = subjects[ix] + " #" + (i + 1),
                ["category"] = categories[ix],
                ["importance"] = ix is 0 or 5 or 6 ? "high" : "normal"
            });
        }

        return rows;
    }

    private (string Key, string Code, string Description)? LastInstalledPack()
    {
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last is null) return null;
        var p = last.Pack;
        return ($"{p.Name}@{p.Version}", p.Code, p.Description);
    }

}


