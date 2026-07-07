using DigitalBrain.Core.Distribution;
using DigitalBrain.Core.Trust;
using DigitalBrain.Marketplace.Contracts;

namespace DigitalBrain.Core;

public static class MarketplaceSeeds
{
    // Single source of truth for the Telegram responder pack. The embodiment test references this const
    // directly so there is no duplicated copy of the pack source.
    public const string TelegramResponderPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;

public sealed class TelegramResponderNeuron : IPackBehavior
{
    // Marketplace NeuroPack name + scope this pack's config is stored under; the responder reads the
    // user's chosen LLM provider/key from (ConfigScope, ConfigPack) to route the AskLlm.
    private const string ConfigPack = "DigitalBrain.Telegram.Responder";
    private const string ConfigScope = "default";

    public PackManifest GetManifest() => new(
        new[] { new SynapseType(TelegramSignals.MessageReceived) },
        new PackConfigField[]
        {
            new("telegram_token", "Bot token",    PackConfigFieldKind.Secret),
            new("llm_provider",   "LLM",          PackConfigFieldKind.Choice, new[] { "ollama", "openai" }),
            new("llm_key",        "API key",       PackConfigFieldKind.Secret,
                DependsOnKey: "llm_provider", DependsOnValue: "openai"),
        });

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not Signal s || s.Name != TelegramSignals.MessageReceived)
            return System.Array.Empty<Synapse>();

        var text   = s.Props.TryGetValue("text",   out var t) ? t?.ToString() ?? "" : "";
        var chatId = s.Props.TryGetValue("chatId", out var c) ? c : null;

        return new Synapse[]
        {
            new AskLlm(
                text,
                TelegramSignals.ReplyRequested,
                new Dictionary<string, object?> { ["chatId"] = chatId },
                ConfigPack,
                ConfigScope)
        };
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Channel,
        null,
        new[] { BundleChannel.Telegram });
}
""";

    // HelloWorldPackCode + Hops demo literals deleted (Musk delete-first).

    // SimpleColorPickerPackCode + Hops demo literals deleted (Musk delete-first).


    // UiGalleryPackCode + Hops demo literals deleted (Musk delete-first).

    public const string KeywordWatcherPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;

public sealed class KeywordWatcherNeuron : IPackBehavior
{
    public PackManifest GetManifest() => new(
        new[] { new SynapseType(TelegramSignals.MessageReceived) },
        null);

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not Signal s || s.Name != TelegramSignals.MessageReceived)
            return System.Array.Empty<Synapse>();

        var text = s.Props.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
        if (!text.StartsWith("remind me", System.StringComparison.OrdinalIgnoreCase))
            return System.Array.Empty<Synapse>();

        return new Synapse[]
        {
            new Signal("ReminderScheduled",
                new Dictionary<string, object?>
                {
                    ["chatId"]   = s.Props.TryGetValue("chatId", out var c) ? c : null,
                    ["reminder"] = text
                })
        };
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Channel,
        null,
        new[] { BundleChannel.Telegram });
}
""";

    public static IReadOnlyList<NeuroPack> LocalUiPacks { get; } =
    [
        new NeuroPack(
            "DigitalBrain.UIKit.ForUI",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Trusted Flutter primitive pack: ForUI theme, shell chrome, icons, cards, forms, sidebars, menus, popovers, tooltips, resizable workbench controls, and common UiSurface renderers.",
            "Preinstalled ForUI primitive kit for rendering DigitalBrain UiSurface contracts. Tier-1 changes require Flutter rebuild/restart.",
            Manifest: new(BundleTier.Content, null, new[] { BundleChannel.InApp })),

        new NeuroPack(
            "DigitalBrain.UI.Workbench",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Startup workbench experience: one canvas with panels for tasks, activity graph, marketplace, INO input, task windows, and timeline.",
            "Preinstalled dynamic Flutter workbench. Layout and surface props stream at runtime."),

        new NeuroPack(
            "DigitalBrain.UI.Graph3D",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Trusted graph primitive pack: compact neuron activity graph, expandable 3D/canvas graph view, ClusterActivity and ThreeDGraphUpdate renderers.",
            "Compact activity graph UI primitive pack for live DigitalBrain cluster state."),

        new NeuroPack(
            "DigitalBrain.UI.CreatorSurfaces",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Surface templates for generated software: user-input, task-window, editor, preview, review, and approval panels driven by UiSurface data.",
            "Reusable dynamic surface templates for software created inside DigitalBrain."),

        new NeuroPack(
            "DigitalBrain.UI.AspireFlutter",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Aspire Flutter integration recipe: one default Flutter UI client resource, start/restart descriptors, and rebuild guidance for trusted primitive pack updates.",
            "Local Flutter client integration pack. Keeps startup to one UI resource by default.",
            Manifest: new(BundleTier.Content, null, new[] { BundleChannel.InApp })),

        new NeuroPack(
            "DigitalBrain.Experience.GmailInsights",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "",
            "Preinstalled Gmail insights experience. Retrieves the last 100 Gmail-shaped messages from the local connector/sample path, summarizes them with the local Ollama model, and emits a chart surface."),

        new NeuroPack(
            MarketplaceUiSurfaces.SalesforceCapabilityPackName,
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "",
            "Salesforce CRM capability: OAuth connection plus read-only Accounts, SOQL query, and CRM summaries through INO.",
            Manifest: new(BundleTier.Channel, null, new[] { BundleChannel.InApp })),

        new NeuroPack(
            "DigitalBrain.Telegram.Responder",
            "1.0.0",
            "digitalbraintech",
            false,
            0.05,
            TelegramResponderPackCode,
            "Telegram bot responder: receives TelegramMessageReceived signals, emits AskLlm to the LLM layer, configurable for Ollama or OpenAI. Install via marketplace, supply token and LLM config.",
            Manifest: new(BundleTier.Channel, null, new[] { BundleChannel.Telegram })),

        new NeuroPack(
            "DigitalBrain.Telegram.KeywordWatcher",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            KeywordWatcherPackCode,
            "Keyword watcher: listens for TelegramMessageReceived, emits ReminderScheduled when the text starts with 'remind me'.",
            Manifest: new(BundleTier.Channel, null, new[] { BundleChannel.Telegram })),

        // demo pack entries (hello-world, simple-color-picker, ui-gallery, xbitcoin) deleted (Musk delete-first; non load-bearing).

        // Dummy and excel-viz demo seeds deleted (Musk delete-first; non load-bearing for core protocol).

    ];

    public static IEnumerable<PublishToMarketplace> LocalUiPackPublishCommands() =>
        LocalUiPacks.Select(ToPublishCommand);

    public static PublishToMarketplace ToPublishCommand(NeuroPack pack) =>
        TrustedPublisher.SignPublishCommand(new(
            pack.Name,
            pack.Version,
            pack.Code,
            pack.OwnerId,
            pack.IsPrivate,
            pack.CommissionRate,
            pack.Description));

    /// <summary>
    /// For developer workflow: publish a new kernel version (triggers self-update on install).
    /// Packaging a kernel "version" in dev is primarily the version bump + description; actual binary ships via Aspire/container.
    /// </summary>
    public static PublishToMarketplace KernelPublishCommand(string? version = null) =>
        new(
            PackName: "kernel",
            Version: version ?? "0.3.1-dev",
            Code: "",
            OwnerId: "digitalbraintech",
            IsPrivate: false,
            CommissionRate: 0.0,
            Description: "Core kernel substrate (dev build).");

    /// <summary>
    /// Dummy full typed-C# behavior pack for testing packaging → publish (share) → install → embody flow during development.
    /// The Code is the complete compilable source for a class implementing IPackBehavior.
    /// </summary>
    // DummyBehaviorPackPublish helper deleted (demo bloat).
}
