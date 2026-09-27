using DigitalBrain.Contracts.Enforcement;
using Orleans.Metadata;

namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.run-authorization")]
internal sealed record RunAuthorization([property: Id(0)] bool Active, [property: Id(1)] CallerContext? Owner);

// The edge asks the file whether a token's run still speaks for it, and on whose behalf.
[Alias("microsoft.csharp.file-edge"), DefaultGrainType("microsoft.csharp.file")]
internal interface ICSharpFileEdge : IGrainWithStringKey
{
    Task<RunAuthorization> Authorize(string runId);
}
