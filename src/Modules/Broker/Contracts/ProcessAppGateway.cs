using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Broker;

// One call from the platform into an installed `process` app. The package and caller travel with
// the call so the broker can verify the signature, re-stamp the caller and check declarations
// before the sandbox is ever started.
public sealed record ProcessCallRequest
{
    public required ProcessAppPackage Package { get; init; }
    public required CallerContext Caller { get; init; }
    public required string Operation { get; init; }
    public IReadOnlyList<string> DataClasses { get; init; } = [];
    public IReadOnlyList<string> EgressHosts { get; init; } = [];
    public string? IntentId { get; init; }
    public decimal EstimatedCompute { get; init; }
}

public sealed record ProcessCallResult
{
    public required bool Allowed { get; init; }
    public required bool Succeeded { get; init; }
    public CallDenial? Denial { get; init; }
    public string? Explanation { get; init; }
    public required string AppId { get; init; }
    public required string Operation { get; init; }
    public string? Output { get; init; }
    public DeclaredObservedDiff? Diff { get; init; }
}

// The only path a process app call may take. Every decision is observable on the result.
public interface IProcessAppGateway
{
    ValueTask<ProcessCallResult> InvokeAsync(ProcessCallRequest request, CancellationToken cancellationToken = default);
}

public static class ProcessAppMeters
{
    public const string SandboxRun = "broker.sandbox_run";
    public const string SandboxRunUnit = "run";
}