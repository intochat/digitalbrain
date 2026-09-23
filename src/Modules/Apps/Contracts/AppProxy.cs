using DigitalBrain.Contracts;
using Orleans.Metadata;

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

[GenerateSerializer, Alias("apps.proxy-outcome")]
public sealed record AppProxyOutcome
{
    [Id(0)] public bool Allowed { get; init; }
    [Id(1)] public string? Denial { get; init; }
    [Id(2)] public string? Explanation { get; init; }
}

// A platform-registered handler for an app operation. Apps never ship grain types; the platform
// maps their declared operations onto neurons it already trusts.
public interface IAppOperationHandler
{
    ValueTask<bool> InvokeAsync(AppProxyRequest request, CancellationToken cancellationToken = default);
}

[Alias("app-proxy"), DefaultGrainType("app-proxy")]
public interface IAppProxy : INeuron
{
    Task<AppProxyOutcome> Invoke(AppProxyRequest request);
}
