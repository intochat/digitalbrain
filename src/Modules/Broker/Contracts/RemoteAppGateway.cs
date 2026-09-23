using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Broker;

// One call from the platform into a publisher-hosted remote app (MCP over HTTP). The manifest and
// caller travel with the call so the broker can re-stamp the caller and check declarations.
public sealed record RemoteCallRequest
{
    public required AppManifest Manifest { get; init; }
    public required CallerContext Caller { get; init; }
    public required string Operation { get; init; }
    public IReadOnlyList<string> DataClasses { get; init; } = [];
    public IReadOnlyList<string> EgressHosts { get; init; } = [];
    public string? IntentId { get; init; }
    public decimal EstimatedCompute { get; init; }
}

public sealed record RemoteAppResponse
{
    public required bool Succeeded { get; init; }
    public string? Output { get; init; }
    public IReadOnlyList<string> UsedDataClasses { get; init; } = [];
    public IReadOnlyList<string> UsedMeters { get; init; } = [];
}

public sealed record RemoteCallResult
{
    public required bool Allowed { get; init; }
    public CallDenial? Denial { get; init; }
    public string? Explanation { get; init; }
    public required string AppId { get; init; }
    public required string Operation { get; init; }
    public string? Output { get; init; }
    public IReadOnlyList<string> EgressHosts { get; init; } = [];
    public DeclaredObservedDiff? Diff { get; init; }
}

// The only path a third-party call may take. Everything the broker decides is observable on the
// returned result; the transport forwards only after every check has passed.
public interface IRemoteAppGateway
{
    ValueTask<RemoteCallResult> InvokeAsync(RemoteCallRequest request, CancellationToken cancellationToken = default);
}

// Publisher-hosted MCP endpoint. Real hosts use HTTP; tests use an in-process deterministic fake.
public interface IRemoteAppTransport
{
    ValueTask<RemoteAppResponse> InvokeAsync(AppManifest manifest, RemoteCallRequest request, CancellationToken cancellationToken = default);
}

public static class BrokerMeters
{
    public const string RemoteCall = "broker.remote_call";
    public const string RemoteCallUnit = "call";
}
