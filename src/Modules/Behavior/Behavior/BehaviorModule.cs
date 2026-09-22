using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;
using Orleans.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Behavior;

[ModuleConfiguration(typeof(BehaviorConfigurationContract))]
public sealed class BehaviorModule : IModule
{
    public static ModuleDefinition Define(BehaviorOptions options)
    {
        var values = new Dictionary<string, string?>
        {
            [BehaviorOptions.SectionName + ":Root"] = options.Root,
            [BehaviorOptions.SectionName + ":Gateways"] = options.Gateways,
            [BehaviorOptions.SectionName + ":ClusterId"] = options.ClusterId,
            [BehaviorOptions.SectionName + ":ServiceId"] = options.ServiceId,
            [BehaviorOptions.SectionName + ":DotnetPath"] = options.DotnetPath,
            [BehaviorOptions.SectionName + ":StartupTimeout"] = options.StartupTimeout.ToString("c"),
            [BehaviorOptions.SectionName + ":StopTimeout"] = options.StopTimeout.ToString("c"),
            [BehaviorOptions.SectionName + ":HeartbeatInterval"] = options.HeartbeatInterval.ToString("c"),
            [BehaviorOptions.SectionName + ":HeartbeatLossTimeout"] = options.HeartbeatLossTimeout.ToString("c"),
            [BehaviorOptions.SectionName + ":MaximumLogBytes"] = options.MaximumLogBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (options.CodeRoot is not null) { values[DigitalBrain.Coding.CodeExecutionOptions.SectionName + ":Root"] = options.CodeRoot; }
        return new(typeof(BehaviorModule), values);
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<BehaviorOptions>().BindConfiguration(BehaviorOptions.SectionName)
            .Configure<IOptions<EndpointOptions>, IOptions<ClusterOptions>>((o, endpoint, cluster) =>
            {
                if (string.IsNullOrWhiteSpace(o.Gateways)) { o.Gateways = new UriBuilder("gwy.tcp", endpoint.Value.AdvertisedIPAddress?.ToString() ?? "127.0.0.1", endpoint.Value.GatewayPort, "0").Uri.ToString(); }
                if (string.IsNullOrWhiteSpace(o.ClusterId)) { o.ClusterId = cluster.Value.ClusterId; }
                if (string.IsNullOrWhiteSpace(o.ServiceId)) { o.ServiceId = cluster.Value.ServiceId; }
            })
            .Validate(o => o.StartupTimeout > TimeSpan.Zero && o.StopTimeout > TimeSpan.Zero
                && o.HeartbeatInterval > TimeSpan.Zero && o.HeartbeatLossTimeout > o.HeartbeatInterval
                && o.MaximumLogBytes > 0, "Invalid behavior execution limits.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton<IBehaviorExecutor, LocalBehaviorExecutor>();
        builder.Services.TryAddSingleton<BehaviorSupervisor>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<BehaviorSupervisor>());
    }
}
