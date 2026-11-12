using DigitalBrain.Contracts;

namespace DigitalBrain.Units;

/// <summary>
/// Durable aggregate neuron managing the deployment lifecycle of a single source key:
/// stages proposals, reacts to confirmation approvals/denials, promotes to live, and serves build images.
/// </summary>
[NeuronAlias("synapse.deployment")]
public interface IUnitDeployment
{
    /// <summary>Proposes a new build or updates slot bindings for the live build.</summary>
    Task<DeployOutcome> Propose(
        StoredBuild build,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings,
        CancellationToken ct = default);

    /// <summary>Returns the build reference for the requested view.</summary>
    [ReadOnlyCall]
    Task<BuildRef?> Ref(UnitView view, CancellationToken ct = default);

    /// <summary>Returns the full stored build for the specified hash, or null.</summary>
    [ReadOnlyCall]
    Task<StoredBuild?> Build(string hash, CancellationToken ct = default);

    /// <summary>Returns the metadata and source files view of the live build, or null.</summary>
    [ReadOnlyCall]
    Task<UnitSourceView?> Source(CancellationToken ct = default);

    /// <summary>Marks a build hash as refused.</summary>
    [OneWayCall]
    Task Refuse(string hash, string reason, CancellationToken ct = default);

    /// <summary>Retires both live and staged versions of this source.</summary>
    Task Retire(CancellationToken ct = default);
}
