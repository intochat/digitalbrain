namespace DigitalBrain.Core;

// Generic protocol carriers for pack-defined events and LLM intents.
// Name/props let pack code ride the wire as a named event bag without polluting Core with domain types.
// Channel-specific names live here so they are first-class datatypes (usable from packs that compile against Core only).

public static class UiSignals
{
    // Central names for UiSurface kinds / events that cross channels.
    // Prefer the UI contract constants for structured surfaces; keep these for Signal-level routing.
    public const string SurfaceEmitted = "UiSurfaceEmitted";
    public const string WidgetTreeUpdated = "UiWidgetTreeUpdated";
}

public static class GoogleSignals
{
    public const string AuthRequested = "GoogleAuthRequested";
    public const string AuthUrl = "GoogleAuthUrl";
    public const string AuthCompleted = "GoogleAuthCompleted";
    public const string GmailFetchRequested = "GmailFetchRequested";
    public const string GmailMessagesReady = "GmailMessagesReady";
}

public static class SalesforceSignals
{
    public const string AuthRequested = "SalesforceAuthRequested";
    public const string AuthUrl = "SalesforceAuthUrl";
    public const string AuthCompleted = "SalesforceAuthCompleted";
    public const string QueryRequested = "SalesforceQueryRequested";
    public const string QueryResultsReady = "SalesforceQueryResultsReady";
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.Signal")]
public record Signal(string Name, IReadOnlyDictionary<string, object?> Props)
    : Synapse(Name, DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.AskLlm")]
public record AskLlm(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string ReplyType,
    [property: Id(2)] IReadOnlyDictionary<string, object?> ReplyProps,
    // Optional reference to the pack config that selects which LLM provider/key answers this ask.
    // When null the global kernel IChatClient is used. Generic — Core ascribes no Telegram meaning here;
    // only a pack that wants per-scope routing sets these.
    [property: Id(3)] string? ConfigPack = null,
    [property: Id(4)] string? ConfigScope = null)
    : Synapse(nameof(AskLlm), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.ILlmResponderNeuron")]
public interface ILlmResponderNeuron : INeuron, IHandle<AskLlm>
{
    // Well-known singleton key. Broadcasts only reach already-activated grains, so production activates
    // this one instance at startup (kernel Program.cs) to subscribe it to the timeline. Callers that need
    // the responder use this key so the AskLlm -> reply Signal path is reachable cluster-wide.
    const string SingletonKey = "llm-responder-main";
}
