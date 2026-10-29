using DigitalBrain.Capabilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Storage;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;

public interface ICloudProbeGrain : IGrainWithStringKey
{
    Task Set(int value);

    Task<int> Get();
}

[GenerateSerializer]
public sealed class CloudProbeState
{
    [Id(0)]
    public int Value { get; set; }
}

public sealed class CloudProbeGrain([PersistentState("probe", IntoChatOSNames.PlatformGrainStorage)] IPersistentState<CloudProbeState> state) : Grain, ICloudProbeGrain
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

/// <summary>A stand-in for BrainLog (os AddBrainStorage): the host's cluster store registration.</summary>
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

internal static class CloudHosts
{
    public const string Vault = "https://test-vault.vault.azure.net/";
    public const string MasterKey = "test-master-key-from-the-in-memory-vault";

    /// <summary>Replaces Key Vault with an in-memory source holding the master key (tests only).</summary>
    public static CloudProfile Profile() => new((builder, _) => builder.AddInMemoryCollection(
        new Dictionary<string, string?> { [IntoChatOSNames.MasterKeyKey] = MasterKey }));

    public static IHost Build(string storage, string home, IClusterStoreRegistration? cluster, string clusterId, params Type[] modules)
        => Build(storage, home, cluster, clusterId, Vault, production: false, modules);

    /// <summary>A null vault leaves IntoChatOS:Cloud:KeyVault unset; production uses the real profile (no vault source swap).</summary>
    public static IHost Build(string storage, string home, IClusterStoreRegistration? cluster, string clusterId, string? vault, bool production, params Type[] modules)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IntoChatOSNames.TopologyKey] = "cloud",
            [IntoChatOSNames.HomeKey] = home,
            [IntoChatOSNames.CloudStorageKey] = storage,
            [IntoChatOSNames.CloudKeyVaultKey] = vault,
            [TopologyContext.SiloPortKey] = Ports.Free().ToString(System.Globalization.CultureInfo.InvariantCulture),
            [TopologyContext.GatewayPortKey] = Ports.Free().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Orleans:ClusterId"] = clusterId,
        });
        if (cluster is not null) { builder.UseClusterStore(cluster); }
        else { builder.UseClusterStore(_ => { }); }
        builder.UseTopology([.. modules.Select(ModuleManifest.Of)], [], production ? new CloudProfile() : Profile());
        builder.UseOrleans(silo =>
        {
            silo.ConfigureTopologySilo();
            silo.Configure<OrleansSdk.Configuration.ClusterOptions>(o => { o.ClusterId = clusterId; o.ServiceId = clusterId; });
            silo.Configure<OrleansSdk.Configuration.EndpointOptions>(o => o.AdvertisedIPAddress = System.Net.IPAddress.Loopback);
        });
        return builder.Build();
    }
}

internal static class Ports
{
    public static int Free()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}

internal sealed class TempHome : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "db-az-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
    }
}
