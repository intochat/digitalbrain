using System.Globalization;
using System.Net;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.CSharp;

[ModuleConfiguration(typeof(CSharpConfigurationContract))]
public sealed class CSharpModule : IModule
{
    // Docker Desktop and Linux engines (via --add-host host-gateway) both route this name to the host.
    internal const string ContainerHost = "host.docker.internal";

    public static ModuleDefinition Define(CSharpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(CSharpModule), new Dictionary<string, string?>
        {
            [CSharpOptions.SectionName + ":Root"] = options.Root,
            [CSharpOptions.SectionName + ":SourceRoot"] = options.SourceRoot,
            [CSharpOptions.SectionName + ":Image"] = options.Image,
            [CSharpOptions.SectionName + ":DockerPath"] = options.DockerPath,
            [CSharpOptions.SectionName + ":Gateways"] = options.Gateways,
            [CSharpOptions.SectionName + ":GatewayRelayHost"] = options.GatewayRelayHost,
            [CSharpOptions.SectionName + ":ClusterId"] = options.ClusterId,
            [CSharpOptions.SectionName + ":ServiceId"] = options.ServiceId,
            [CSharpOptions.SectionName + ":DockerTimeout"] = options.DockerTimeout.ToString("c", CultureInfo.InvariantCulture),
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<CSharpOptions>().BindConfiguration(CSharpOptions.SectionName)
            .Configure<IOptions<EndpointOptions>, IOptions<ClusterOptions>>((o, endpoint, cluster) =>
            {
                o.Root ??= Path.Combine(Path.GetTempPath(), "digitalbrain-csharp");
                o.SourceRoot ??= RepositoryRoot.Find();
                if (string.IsNullOrWhiteSpace(o.Gateways))
                {
                    var advertised = endpoint.Value.AdvertisedIPAddress ?? IPAddress.Loopback;
                    o.Gateways = new UriBuilder("gwy.tcp", advertised.ToString(), endpoint.Value.GatewayPort, "0").Uri.ToString();
                    if (IPAddress.IsLoopback(advertised)) { o.GatewayRelayHost ??= ContainerHost; }
                }
                if (string.IsNullOrWhiteSpace(o.ClusterId)) { o.ClusterId = cluster.Value.ClusterId; }
                if (string.IsNullOrWhiteSpace(o.ServiceId)) { o.ServiceId = cluster.Value.ServiceId; }
            })
            .Validate(o => o.DockerTimeout > TimeSpan.Zero && !string.IsNullOrWhiteSpace(o.Image), "Invalid CSharp execution settings.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.TryAddSingleton<ICSharpRunner, DockerCSharpRunner>();
        builder.Services.TryAddSingleton<CSharpContractCatalog>();
    }
}
