using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.file-state")]
internal sealed record CSharpFileState
{
    [Id(0)] public string Source { get; init; } = "";
    [Id(1)] public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);
    // A new sandbox run per Start, so a stale run can never be addressed again.
    [Id(2)] public string RunId { get; init; } = "";
    [Id(3)] public bool ShouldRun { get; init; }
    // Consecutive non-zero exits since the last Start.
    [Id(4)] public int Failures { get; init; }
    // The principal who started it; production keys its sandbox session (one container per user) by it.
    [Id(5)] public string Owner { get; init; } = "";
    // Set by Arm: runs start on this trigger's signals instead of staying up.
    [Id(6)] public CSharpTrigger? Trigger { get; init; }
    // The caller who started or armed it; the script edge speaks for this caller as an app.
    [Id(7)] public CallerContext? OwnerContext { get; init; }
    // Durable subscriptions the current script registered by streaming; they outlive its runs.
    [Id(8)] public ScriptSubscription[] Subscriptions { get; init; } = [];
    [Id(9)] public string? AppId { get; init; }
    [Id(10)] public string? AppBrain { get; init; }
}
