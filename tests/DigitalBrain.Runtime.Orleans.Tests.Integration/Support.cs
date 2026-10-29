using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Capabilities;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration;

public interface IProbeGrain : IGrainWithStringKey
{
    Task Set(int value);

    Task<int> Get();
}

[GenerateSerializer]
public sealed class ProbeState
{
    [Id(0)]
    public int Value { get; set; }
}

public sealed class ProbeGrain([PersistentState("probe", IntoChatOSNames.PlatformGrainStorage)] IPersistentState<ProbeState> state) : Grain, IProbeGrain
{
    public async Task Set(int value)
    {
        state.State.Value = value;
        await state.WriteStateAsync();
    }

    public Task<int> Get() => Task.FromResult(state.State.Value);
}

[Module("drive-test")]
public sealed class DriveModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [Need.State("drive")], [], []);
}

[Module("circle-test")]
public sealed class CircleModule : Module
{
    public static ModuleManifest Manifest { get; } = new([new CapabilityId("circle", 1)], [], [], [], []);
}

/// <summary>A stand-in for BrainLog: the host's cluster store registration.</summary>
public sealed class FakeClusterStore : IClusterStoreRegistration, IGrainStorage
{
    public int Registered { get; private set; }

    public void Register(ISiloBuilder silo)
    {
        Registered++;
        silo.Services.AddKeyedSingleton<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage, this);
    }

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;
}

internal sealed class TempHome : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "db-int-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
    }
}
