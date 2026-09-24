namespace DigitalBrain.Apps;

// An app exposes operations only through this platform proxy, so an installed app never adds a grain
// type to the silo. The proxy re-stamps the caller as an app and asks the one call filter to decide.
[GenerateSerializer, Alias("apps.proxy-request")]
public sealed record AppProxyRequest
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Operation { get; init; }
    [Id(2)] public required string TargetNeuron { get; init; }
    [Id(3)] public required string PrincipalId { get; init; }
    [Id(4)] public required string AccountId { get; init; }
    [Id(5)] public required string WorkspaceId { get; init; }
    [Id(6)] public IReadOnlyList<string> SemanticTypeIds { get; init; } = [];
    [Id(7)] public decimal EstimatedCompute { get; init; }
    [Id(8)] public bool HasSideEffects { get; init; }
}
