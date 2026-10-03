using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.CSharp;

// One durable subscription a script declared by opening a signal stream. Pending holds the JSON of
// signals published while nothing was draining; delivery removes them, so handlers must be idempotent.
// Arrays, not interface lists: grain storage round-trips this state through JSON.
[GenerateSerializer, Alias("microsoft.csharp.script-subscription")]
internal sealed record ScriptSubscription(
    [property: Id(0)] string Neuron,
    [property: Id(1)] string Signal,
    [property: Id(2)] string[] Pending);

[GenerateSerializer, Alias("microsoft.csharp.pending-arrived")]
internal sealed record CSharpPendingArrived([property: Id(0)] string FileId) : Signal;
