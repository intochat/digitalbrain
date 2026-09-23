namespace DigitalBrain.Broker;

// A process app never gets a direct network path or an Orleans gateway. It runs inside a per-app
// container whose only inputs are its image, its declared command and the platform-set environment.
public enum SandboxNetworkPolicy
{
    Denied = 0,
    BrokerOnly = 1,
}

public sealed record SandboxLimits
{
    public double CpuCount { get; init; } = 0.5;
    public long MemoryBytes { get; init; } = 256L * 1024 * 1024;
    public int ProcessCount { get; init; } = 16;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record SandboxSpec
{
    public required string AppId { get; init; }
    public required string Image { get; init; }
    public IReadOnlyList<string> Command { get; init; } = [];
    public SandboxLimits Limits { get; init; } = new();
    public SandboxNetworkPolicy Network { get; init; } = SandboxNetworkPolicy.Denied;
    public bool OrleansGateway { get; init; }
    public bool ReadOnlyRootFilesystem { get; init; } = true;
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

public sealed record SandboxRunRequest
{
    public required SandboxSpec Spec { get; init; }
    public required string Operation { get; init; }
    public string? Input { get; init; }
}

public sealed record SandboxRunResult
{
    public required bool Succeeded { get; init; }
    public string? Output { get; init; }
    public int ExitCode { get; init; }
    public IReadOnlyList<string> UsedDataClasses { get; init; } = [];
    public IReadOnlyList<string> UsedMeters { get; init; } = [];
    public string? Failure { get; init; }
}

// The adapter seam. Docker implements it in production; a fake implements it in tests.
public interface ISandboxRuntime
{
    bool IsAvailable { get; }

    ValueTask<SandboxRunResult> RunAsync(SandboxRunRequest request, CancellationToken cancellationToken = default);
}