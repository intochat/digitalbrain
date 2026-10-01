using DigitalBrain.Contracts.Enforcement;
using Orleans.Metadata;

namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.run-authorization")]
internal sealed record RunAuthorization([property: Id(0)] bool Active, [property: Id(1)] CallerContext? Owner);

// The edge asks the file whether a token's run still speaks for it, and on whose behalf. It also
// records the durable subscriptions a script declares by streaming, and drains their buffers.
[Alias("microsoft.csharp.file-edge"), DefaultGrainType("microsoft.csharp.file")]
internal interface ICSharpFileEdge : IGrainWithStringKey
{
    Task<RunAuthorization> Authorize(string runId);
    Task Subscribed(string runId, string neuron, string signal);
    Task<IReadOnlyList<string>> DrainPending(string runId, string neuron, string signal);
}
