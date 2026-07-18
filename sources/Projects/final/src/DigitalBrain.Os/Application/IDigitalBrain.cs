using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Os.Application;

public interface IDigitalBrain : INeuron
{
    public static Func<CancellationToken, Task<IDigitalBrainClient>> StartNewResolver { get; set; }
        = cancellationToken => throw new InvalidOperationException("DigitalBrain bootstrap not registered.");

    static Task<IDigitalBrainClient> StartNew(CancellationToken cancellationToken = default) => StartNewResolver(cancellationToken);

    Task SendAsync(Synapse synapse, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NeuronId>> ListSubscribersAsync(string synapseTypeName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListActiveNeuronTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Synapse>> GetRecentHistoryAsync(int max = 10, CancellationToken cancellationToken = default);

    // For unbounded causal replay in AI (LlmAgent/self-improve/Creator proposals, tools, history) per domain.
    Task<IReadOnlyList<Synapse>> GetFullJournalAsync(CancellationToken cancellationToken = default);

    Task<BundleInstalled> InstallBundleAsync(InstallBundle command, CancellationToken cancellationToken = default);

    // Bundle sharing + install on cluster (publish makes id visible via state; install by id activates the bundle's neurons/synapses so they participate on timeline + dispatch. N+1 growth is the Core Law marketplace proof.
    Task PublishBundleAsync(string bundleId, string? description = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListPublishedBundlesAsync(CancellationToken cancellationToken = default);
    Task InstallBundleAsync(string bundleId, CancellationToken cancellationToken = default);

    Task UninstallBundleAsync(string bundleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListInstalledBundlesAsync(CancellationToken cancellationToken = default);

    Task RunExperienceAsync(string experienceId, CancellationToken cancellationToken = default);

    // Launch resolver extension point (additive; hosts wire real for AspireHosted/WorldId fork/dupe; default keeps sim fast path).
    public static Func<DigitalBrainStartOptions, CancellationToken, Task<IDigitalBrainClient>> LaunchResolver { get; set; } = (options, cancellationToken) =>
        (options.Mode == DigitalBrainLaunchMode.Simulation && string.IsNullOrWhiteSpace(options.WorldId))
            ? StartNew(cancellationToken)
            : Task.FromResult<IDigitalBrainClient>(new DefaultDigitalBrainClient());

    /// unified start (drives simulation vs aspire-hosted / fork / from-moment via options)
    static Task<IDigitalBrainClient> Start(DigitalBrainStartOptions options, CancellationToken cancellationToken = default)
    {
        // options.Mode == AspireHosted or WorldId -> launcher (in Sdk) does Process on AppHost + real client (under the hood IAspire.StartNew or direct spawn)
        // options.WorldId for duplication/branch + seed from history in future micros
        // default keeps current resolver (fast simulation in start.cs for zero-reg inner loop)
        return LaunchResolver(options, cancellationToken);
    }

    Task<WorldConnectionInfo> StartWorldAsync(string worldId, CancellationToken cancellationToken = default);
    Task<WorldConnectionInfo?> GetWorldConnectionAsync(string worldId, CancellationToken cancellationToken = default);

    Task<WorldConnectionInfo?> GetCurrentWorldAsync(CancellationToken cancellationToken = default) => Task.FromResult<WorldConnectionInfo?>(null);
    Task<WorldConfig?> GetWorldConfigAsync(string? worldId = null, CancellationToken cancellationToken = default) => Task.FromResult<WorldConfig?>(null);

    Task<BrainIdentity> GetIdentityAsync(CancellationToken cancellationToken = default);
    Task<string> SignAsync(string data, CancellationToken cancellationToken = default);

    // Fork a new brain by replaying parent journal up to a point (S06 dedup + fork rides quarantine machinery for Stage 6).
    // The fork uses the same isolation as quarantine (new key or world), allows risky installs in the fork, promote back if green.
    Task<WorldConnectionInfo> ForkBrainAsync(string parentBrainKey, string newBrainName, DateTimeOffset? upTo = null, CancellationToken cancellationToken = default);
}

[GenerateSerializer]
public sealed record WorldConfig(string WorldId, string ClusterId, string ServiceId, string GatewayAddress, string? GemmaModel = null, string? NemotronModel = null);

// IAsyncDisposable so a launched world tears down its spawned AppHost process + Orleans client host
// when the caller is done — without it, the child process is orphaned (the cause of leaked dotnet hosts).
public interface IDigitalBrainClient : IAsyncDisposable
{
    string? DashboardUrl => null;
    IClusterClient? ClusterClient => null;
    WorldConnectionInfo? CurrentWorld => null;
}

public sealed record DigitalBrainStartOptions
{
    public DigitalBrainLaunchMode Mode { get; init; } = DigitalBrainLaunchMode.Simulation;
    public string? WorldId { get; init; }
    public string? GatewayAddress { get; init; }
    // Future: SnapshotPoint, ForkFromHistory, ExtraResources etc for "start from some moment" / "duplication of the world".
}

public enum DigitalBrainLaunchMode
{
    Simulation,     // fast in-memory solo (default for dotnet run start.cs inner loop)
    AspireHosted,   // real durable cluster via AppHost / IAspire launch (same track as aspire run)
    ConnectExisting
}

// Default marker for sim path (self-explanatory; real launcher returns one that populates ClusterClient).
public sealed class DefaultDigitalBrainClient : IDigitalBrainClient
{
    public string? DashboardUrl => null;
    public IClusterClient? ClusterClient => null;
    public WorldConnectionInfo? CurrentWorld => null;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// World descriptor for brain-orchestrated clusters (root maintains registry; returned by StartWorld/GetWorld so callers can build IClusterClient to the child without static ports in AppHost).
[GenerateSerializer]
public sealed record WorldConnectionInfo(
    [property: Id(0)] string WorldId,
    [property: Id(1)] string ClusterId,
    [property: Id(2)] string ServiceId,
    [property: Id(3)] string GatewayAddress,
    [property: Id(4)] string? DashboardUrl = null
) : Synapse;

// Expressive self-documenting config replacing raw $env hacks. 
// DefaultSetup (via KernelSetup) is the primary for real local gemma3:1b (no env required; cross-platform CPU friendly for Windows + docker ollama).
// Base owns Synapse-related bootstrap (stream provider name etc). No boilerplate summaries.
public class Setup
{
    public virtual string GemmaModel => "gemma3:1b";
    public virtual bool UseDemoMode => false;
    public virtual string SynapseStreamProviderName => "DigitalBrainTimeline";
}

public class KernelSetup : Setup { }

public class DefaultSetup : KernelSetup { }

public class TestSetup : KernelSetup
{
    public override bool UseDemoMode => true;
}
