using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// The <c>cluster</c> store before the host supplies it. The cluster store is BrainLog, never plaintext files; every
/// member throws naming AddBrainStorage and the hook that registers it.
/// </summary>
public sealed class UnregisteredClusterStorage : IGrainStorage
{
    /// <summary>The message every member throws.</summary>
    public const string Message = "The cluster store is BrainLog; call AddBrainStorage through "
        + ClusterStoreHosting.HookName + " before UseTopology.";

    /// <inheritdoc />
    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => throw new InvalidOperationException(Message);

    /// <inheritdoc />
    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => throw new InvalidOperationException(Message);

    /// <inheritdoc />
    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => throw new InvalidOperationException(Message);
}
