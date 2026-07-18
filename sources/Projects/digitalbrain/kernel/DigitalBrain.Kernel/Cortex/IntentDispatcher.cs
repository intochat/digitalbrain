using System.Text.Json.Nodes;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Introspector;
using Orleans.Streams;
using Orleans.Streams.Core;
using DigitalBrain.SDK.Canvas;
using DigitalBrain.SDK.Google;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Dynamic;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.NemoChat;
using DigitalBrain.WidgetCanvas;
using DigitalBrain.Runtime.Runtime.Settings;

namespace DigitalBrain.Kernel.Cortex;

public interface IIntentDispatcher : IGrainWithGuidKey
{
    Task EnsureActivatedAsync();

    // Pushes the default home dashboard onto the home feed. Called by the
    // gateway the moment a shell attaches to WatchHomeFeed (the only reliable
    // "shell connected" hook), so the cards land on the live subscriber.
    Task ComposeDashboardAsync();
}

// Singleton grain in the kernel that listens on the global synapse timeline
// and translates each IntentClassified into the action synapse for that intent.
// Lives only in the kernel project so Orleans places it on the kernel silo.
// Fires its action with a fresh correlation id so the upstream ClassifyIntent
// caller's correlation stays clean for its own reply.
[ImplicitStreamSubscription(Neuron.GlobalTimelineNamespace)]
public sealed class IntentDispatcher(
    IGrainFactory grains,
    HomeFeedBus homeFeed,
    ILogger<IntentDispatcher> logger)
    : Grain, IIntentDispatcher, IStreamSubscriptionObserver, IAsyncObserver<Synapse>
{
    public Task EnsureActivatedAsync() => Task.CompletedTask;

    // Stable, distinct correlationIds per widget: distinct so the cards land as
    // five separate panels (a shared id collapses them onto one), stable so the
    // shell reuses each panel in place across reconnects instead of duplicating.
    static readonly Guid ClockCardId    = new("d1000000-0000-0000-0000-000000000001");
    static readonly Guid FlightCardId   = new("d1000000-0000-0000-0000-000000000002");
    static readonly Guid ReminderCardId = new("d1000000-0000-0000-0000-000000000003");
    static readonly Guid CanvasCardId   = new("d1000000-0000-0000-0000-000000000004");
    static readonly Guid SettingsCardId = new("d1000000-0000-0000-0000-000000000005");

    public async Task ComposeDashboardAsync()
    {
        var cause = Guid.NewGuid();

        // Clock, the 3D flight globe, and the reminder render straight from their
        // compiled ui: surfaces — the same catalog path the NLU intents use, and
        // a direct broadcast to every attached shell.
        await BroadcastNeuronCardAsync(WidgetCanvasNeurons.Clock, ClockCardId, cause, null);
        await BroadcastNeuronCardAsync(WidgetCanvasNeurons.Flight, FlightCardId, cause, null);
        await BroadcastNeuronCardAsync(WidgetCanvasNeurons.Reminder, ReminderCardId, cause, 25 * 60);

        // The 3D visualization canvas and the settings panel build data-rich
        // cards from their own state, so route their request synapses and let
        // each neuron emit its surface onto the home feed.
        var gateway = grains.GetGrain<IGatewayNeuron>(GatewayNeuron.GatewayInstanceKey);

        await gateway.RouteAsync(new OpenCanvasRequest(UserId: "me", SceneName: "default")
        {
            Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(), correlationId: CanvasCardId, causationId: cause,
                callerNeuronId: Guid.Empty, callerNeuronType: nameof(IntentDispatcher),
                receiverNeuronId: Guid.Empty, receiverNeuronType: "CanvasNeuron",
                timestamp: TimeProvider.System.GetUtcNow())
        });

        await gateway.RouteAsync(new RequestSettingsCard
        {
            Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(), correlationId: SettingsCardId, causationId: cause,
                callerNeuronId: Guid.Empty, callerNeuronType: nameof(IntentDispatcher),
                receiverNeuronId: Guid.Empty, receiverNeuronType: "DigitalBrain.Kernel.Settings.SettingsNeuron",
                timestamp: TimeProvider.System.GetUtcNow())
        });

        logger.LogInformation("Dashboard composed: clock, flight, reminder, canvas and settings cards pushed.");
    }

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var handle = handleFactory.Create<Synapse>();
        try
        {
            await handle.ResumeAsync(this);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resuming subscription with cached token failed in IntentDispatcher. Falling back to fresh subscribe.");
            await handle.ResumeAsync(this, null);
        }
    }

    public async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        // A reminder panel's snooze button re-arms the reminder: the .ino's
        // `on snooze` re-emits `armed`, and here the kernel re-pushes the panel's
        // surface (same correlation id ⇒ the client updates the panel in place)
        // counting down from the new minutes.
        if (item is Snooze snooze)
        {
            await BroadcastNeuronCardAsync(
                WidgetCanvasNeurons.Reminder, snooze.CorrelationId, snooze.SynapseId, snooze.Minutes * 60);
            return;
        }

        if (item is not IntentClassified intent) return;
        if (intent.Intent == KnownIntent.Unknown)
        {
            var gateway = grains.GetGrain<IGatewayNeuron>(GatewayNeuron.GatewayInstanceKey);
            var fresh = Guid.NewGuid();
            var req = new AuthorInoNeuronRequest(
                Intent: intent.Transcript,
                SuggestedFqn: $"Dynamic.{Slugify(intent.Transcript)}",
                LlmModelKey: "ino-local",
                MaxAttempts: 5
            ) { Headers = SynapseMetadata.Create(
                synapseId: fresh,
                correlationId: intent.CorrelationId,
                causationId: intent.SynapseId,
                callerNeuronId: Guid.Empty,
                callerNeuronType: nameof(IntentDispatcher),
                receiverNeuronId: fresh,
                receiverNeuronType: "InoCreatorNeuron",
                timestamp: TimeProvider.System.GetUtcNow()
            ) };
            await gateway.RouteAsync(req);
            logger.LogInformation(
                "Intent dispatcher: Unknown intent routed to InoCreatorNeuron (AuthorInoNeuronRequest) for '{Transcript}'.",
                intent.Transcript);
            return;
        }

        var action = BuildActionSynapse(intent);
        if (action is null) return;

        var actionGateway = grains.GetGrain<IGatewayNeuron>(GatewayNeuron.GatewayInstanceKey);
        await actionGateway.RouteAsync(action);

        await TryPushWidgetCanvasCardAsync(intent);
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        if (ex is QueueCacheMissException || ex.GetType().FullName == "Orleans.Streams.QueueCacheMissException")
        {
            logger.LogWarning(ex, "Transient stream cache miss in IntentDispatcher; Orleans pulling agent will recover.");
        }
        else
        {
            logger.LogError(ex, "Intent dispatcher stream subscription error.");
        }
        return Task.CompletedTask;
    }

    static Synapse? BuildActionSynapse(IntentClassified intent) => intent.Intent switch
    {
        KnownIntent.GetLastNGmailSenders => BuildStoreSendersRequest(intent),
        KnownIntent.ExplainQuery         => BuildExplainRequest(intent),
        KnownIntent.LifePlanning         => BuildBrainstormRequest(intent),
        KnownIntent.FindVideo            => BuildFindVideoRequest(intent),
        KnownIntent.OpenCanvas           => BuildOpenCanvasRequest(intent),
        KnownIntent.CreateFolder         => BuildCreateFolderRequest(intent),
        KnownIntent.NemoChat             => BuildNemoChatRequest(intent),
        KnownIntent.SetClock             => BuildSetClock(intent),
        KnownIntent.RemindMe             => BuildRemindMe(intent),
        KnownIntent.ShowFlight           => BuildShowFlight(intent),
        _ => null,
    };

    static SetClock BuildSetClock(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var timezone = intent.Parameters.GetValueOrDefault("Timezone", "local");
        return new SetClock(Timezone: timezone) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: WidgetCanvasNeurons.Clock,
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    static RemindMe BuildRemindMe(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var minutes = intent.Parameters.TryGetValue("Minutes", out var raw)
            && int.TryParse(raw, out var parsed) ? parsed : 10;
        return new RemindMe(Minutes: minutes) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: WidgetCanvasNeurons.Reminder,
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    static ShowFlight BuildShowFlight(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var code = intent.Parameters.GetValueOrDefault("Code", "");
        return new ShowFlight(Code: code) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: WidgetCanvasNeurons.Flight,
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    static CreateFolderRequest BuildCreateFolderRequest(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var prompt = intent.Parameters.GetValueOrDefault("Prompt", intent.Transcript);
        return new CreateFolderRequest(Prompt: prompt) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: "NavigatorNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    // ReceiverNeuronType is the YouTube neuron's stream namespace
    // (== nameof(YouTubeSearchNeuron)); kept as a literal so the kernel does
    // not reference the Google silo project, matching the GmailDigestNeuron
    // routing convention above.
    static FindVideoRequest BuildFindVideoRequest(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var query = intent.Parameters.GetValueOrDefault("Query", intent.Transcript);
        var userAccountId = intent.Parameters.GetValueOrDefault("UserAccountId", "me");
        return new FindVideoRequest(Query:              query,
        UserAccountId:      userAccountId) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: "YouTubeSearchNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    // ReceiverNeuronType is the Canvas neuron's stream namespace
    // (== nameof(CanvasNeuron)); kept as a literal so the kernel does
    // not reference the Canvas silo project, matching the GmailDigestNeuron /
    // YouTubeSearchNeuron routing convention above.
    static OpenCanvasRequest BuildOpenCanvasRequest(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var sceneName = intent.Parameters.GetValueOrDefault("SceneName", "");
        var userId = intent.Parameters.GetValueOrDefault("UserId", "me");
        return new OpenCanvasRequest(UserId:             userId,
        SceneName:          sceneName) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: "CanvasNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    static BrainstormRequest BuildBrainstormRequest(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var prompt = intent.Parameters.GetValueOrDefault("Prompt", intent.Transcript);
        return new BrainstormRequest(Prompt:             prompt,
        MinOptions:         2,
        MaxOptions:         4) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: AiNeuronTypes.BrainstormNeuron,
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    static StoreLastNGmailSendersRequest BuildStoreSendersRequest(IntentClassified intent)
    {
        var n = intent.Parameters.TryGetValue("N", out var nString)
            && int.TryParse(nString, out var parsed) ? parsed : 5;
        var databaseId = intent.Parameters.GetValueOrDefault("DatabaseId", "email-senders");
        var userAccountId = intent.Parameters.GetValueOrDefault("UserAccountId", "me");

        var fresh = Guid.NewGuid();
        return new StoreLastNGmailSendersRequest(UserAccountId: userAccountId,
        N: n,
        DatabaseId: databaseId) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: "GmailDigestNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    static ExplainDecisionRequest BuildExplainRequest(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var query = intent.Parameters.GetValueOrDefault("Query", intent.Transcript);
        var userId = intent.Parameters.GetValueOrDefault("UserId", "default");
        return new ExplainDecisionRequest(NaturalLanguageQuery: query,
        UserId:             userId) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: "IntrospectorNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    static NemoChatRequest BuildNemoChatRequest(IntentClassified intent)
    {
        var fresh = Guid.NewGuid();
        var prompt = intent.Parameters.GetValueOrDefault("Prompt", intent.Transcript);
        return new NemoChatRequest(Prompt: prompt) { Headers = SynapseMetadata.Create(
            synapseId: fresh,
            correlationId: intent.CorrelationId,
            causationId: intent.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntentDispatcher),
            receiverNeuronId: fresh,
            receiverNeuronType: "NemoChatNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };
    }

    // FQNs of the authored .ino widget-canvas neurons. Kept as literals so the
    // kernel does not reference the sample neurons by type; the gateway resolves
    // each synapse to its interpreted neuron by contract type, and these are the
    // catalog keys the on-intent push looks up the ui: surface under.
    static class WidgetCanvasNeurons
    {
        public const string Clock    = "DigitalBrain.WidgetCanvas.ClockNeuron";
        public const string Reminder = "DigitalBrain.WidgetCanvas.ReminderNeuron";
        public const string Flight   = "DigitalBrain.WidgetCanvas.FlightNeuron";
    }

    static string? WidgetCanvasNeuronFor(KnownIntent intent) => intent switch
    {
        KnownIntent.SetClock   => WidgetCanvasNeurons.Clock,
        KnownIntent.RemindMe   => WidgetCanvasNeurons.Reminder,
        KnownIntent.ShowFlight => WidgetCanvasNeurons.Flight,
        _ => null,
    };

    // On a widget-canvas intent, push the neuron's compiled ui: surface onto the
    // home feed so a panel appears immediately (UI-is-data, V5-4).
    async Task TryPushWidgetCanvasCardAsync(IntentClassified intent)
    {
        var fqn = WidgetCanvasNeuronFor(intent.Intent);
        if (fqn is null) return;

        int? countdownSeconds = intent.Intent == KnownIntent.RemindMe
            ? (intent.Parameters.TryGetValue("Minutes", out var raw) && int.TryParse(raw, out var m) ? m : 10) * 60
            : null;

        await BroadcastNeuronCardAsync(fqn, intent.CorrelationId, intent.SynapseId, countdownSeconds);
    }

    // Fetches a neuron's compiled ui: surface from the catalog and broadcasts it
    // as a home-feed card. When countdownSeconds is set, the CountdownClock's live
    // slots are filled so its hands run backward from the requested duration
    // rather than a static default. Same correlationId ⇒ the client updates the
    // existing panel in place (the snooze re-arm path).
    async Task BroadcastNeuronCardAsync(string fqn, Guid correlationId, Guid causationId, int? countdownSeconds)
    {
        string? layoutJson;
        try
        {
            var catalog = grains.GetGrain<IBrainCatalog>(BrainScopeHelper.GlobalScope);
            var entries = await catalog.ListRegisteredAsync();
            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.TypeFullName, fqn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Id.Value, fqn, StringComparison.OrdinalIgnoreCase));
            layoutJson = entry?.UiLayoutJson;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Widget-canvas: failed to read ui: surface for {Fqn} from the catalog.", fqn);
            return;
        }

        if (string.IsNullOrEmpty(layoutJson))
        {
            logger.LogWarning(
                "Widget-canvas: no ui: surface registered for {Fqn}; is its .ino loaded? Panel not pushed.", fqn);
            return;
        }

        if (countdownSeconds is int seconds)
        {
            try
            {
                var root = JsonNode.Parse(layoutJson);
                if (root is not null)
                {
                    ApplyCountdownValues(root, seconds, TimeProvider.System.GetUtcNow().ToString("O"));
                    layoutJson = root.ToJsonString();
                }
            }
            catch
            {
                // Best-effort: an unrecognised shape ships with its authored defaults.
            }
        }

        var card = new RfwCard(
            LibraryName: "uikit",
            RootWidget: "UiKit",
            DataJson: layoutJson)
        {
            Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: correlationId,
                causationId: causationId,
                callerNeuronId: Guid.Empty,
                callerNeuronType: fqn,
                receiverNeuronId: Guid.Empty,
                receiverNeuronType: "HomeFeed",
                timestamp: TimeProvider.System.GetUtcNow())
        };

        await homeFeed.BroadcastAsync(card);
        logger.LogInformation("Widget-canvas: pushed ui: surface for {Fqn} onto the home feed.", fqn);
    }

    static void ApplyCountdownValues(JsonNode node, int durationSeconds, string startedAtUtc)
    {
        switch (node)
        {
            case JsonObject obj:
                var name = obj.TryGetPropertyValue("name", out var n) ? n?.GetValue<string>() : null;
                if (string.Equals(name, "CountdownClock", StringComparison.OrdinalIgnoreCase)
                    && obj.TryGetPropertyValue("arguments", out var argsNode)
                    && argsNode is JsonObject args)
                {
                    args["durationSeconds"] = durationSeconds;
                    args["startedAtUtc"] = startedAtUtc;
                }
                foreach (var (_, child) in obj)
                    if (child is not null) ApplyCountdownValues(child, durationSeconds, startedAtUtc);
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    if (item is not null) ApplyCountdownValues(item, durationSeconds, startedAtUtc);
                break;
        }
    }

    static string Slugify(string s) =>
        string.Concat(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' '))
              .Replace(' ', '-')
              .Trim('-');
}
