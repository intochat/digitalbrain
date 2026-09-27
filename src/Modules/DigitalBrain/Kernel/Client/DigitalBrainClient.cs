using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;

namespace DigitalBrain.Client;

public static class DigitalBrainClient
{
    public static async Task<DigitalBrainConnection> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        GatewayRelay? relay = null;
        builder.UseOrleansClient(client =>
        {
            client.AddDigitalBrain();
            if (Assembly.GetEntryAssembly() is { } script)
            {
                // Script assemblies and raw references carry no Orleans ApplicationPart attributes.
                client.Services.AddSerializer(serializer =>
                {
                    serializer.AddAssembly(script);
                    foreach (var reference in script.GetReferencedAssemblies()) { serializer.AddAssembly(Assembly.Load(reference)); }
                });
            }
            if (builder.Configuration["Gateways"] is not { Length: > 0 } gateways)
            {
                if (!builder.Configuration.GetValue<bool>("LocalDevelopment"))
                { throw new InvalidOperationException("Supply Gateways, ClusterId and ServiceId, or set LocalDevelopment=true."); }
                client.UseLocalhostClustering();
                return;
            }
            Uri[] endpoints = [.. gateways.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(gateway => new Uri(gateway))];
            if (builder.Configuration["GatewayRelayHost"] is { Length: > 0 } relayHost) { relay = GatewayRelay.Start(endpoints, relayHost); }
            client.UseStaticClustering(options => options.Gateways = [.. endpoints]);
            client.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = builder.Configuration["ClusterId"] ?? throw new InvalidOperationException("ClusterId is required.");
                options.ServiceId = builder.Configuration["ServiceId"] ?? throw new InvalidOperationException("ServiceId is required.");
            });
        });
        IHost? host = null;
        try
        {
            host = builder.Build();
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return new DigitalBrainConnection(host, relay);
        }
        catch
        {
            host?.Dispose();
            if (relay is not null) { await relay.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }
}
