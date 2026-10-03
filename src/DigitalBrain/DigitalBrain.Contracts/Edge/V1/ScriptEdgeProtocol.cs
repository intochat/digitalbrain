using System.Text.Json;

namespace DigitalBrain.Contracts.Edge.V1;

// The wire between a script and the brain's script edge. Scripts never hold an Orleans connection:
// every neuron call and signal subscription goes over HTTPS with the run's token.
public static class ScriptEdgeProtocol
{
    public const string EdgeSetting = "DigitalBrain:Edge";
    public const string TokenSetting = "DigitalBrain:Token";
    public const string Invoke = "scripts/v1/invoke";
    public const string RefusalHeader = "X-DigitalBrain-Refusal";
    public const string Signals = "scripts/v1/signals";
    // Sent first on every signal stream: a server-sent events response flushes its headers only with
    // its first event, and the client returns the subscription once the headers arrive.
    public const string ReadyEvent = "ready";
    public const string SignalEvent = "signal";

    public static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

}

public sealed record ScriptInvocation(string Contract, string Key, string Method, JsonElement[] Arguments);
