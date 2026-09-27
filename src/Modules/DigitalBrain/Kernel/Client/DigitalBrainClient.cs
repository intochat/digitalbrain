using System.Diagnostics;
using System.Net;
using System.Reflection;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;

namespace DigitalBrain.Core;

public static class DigitalBrainClient
{
    private static readonly ActivitySource ScriptActivity = new("DigitalBrain.Client");

    public static async Task<DigitalBrainConnection> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var scriptAssembly = Assembly.GetEntryAssembly();
        GatewayRelay? relay = null;
        builder.UseOrleansClient(client =>
        {
            client.AddActivityPropagation();
            client.AddDigitalBrain();
            if (scriptAssembly is not null)
            {
                // Script assemblies and raw references carry no Orleans ApplicationPart attributes.
                client.Services.AddSerializer(serializer =>
                {
                    serializer.AddAssembly(scriptAssembly);
                    foreach (var reference in scriptAssembly.GetReferencedAssemblies())
                    { serializer.AddAssembly(Assembly.Load(reference)); }
                });
            }
            var gateways = builder.Configuration["Gateways"];
            if (string.IsNullOrWhiteSpace(gateways))
            {
                if (!builder.Configuration.GetValue<bool>("LocalDevelopment"))
                { throw new InvalidOperationException("Supply Gateways, ClusterId and ServiceId, or set LocalDevelopment=true."); }
                client.UseLocalhostClustering();
                return;
            }
            var endpoints = GatewayEndpoints(gateways);
            if (builder.Configuration["GatewayRelayHost"] is { Length: > 0 } relayHost) { relay = GatewayRelay.Start(endpoints, relayHost); }
            client.UseStaticClustering(options => options.Gateways = [.. endpoints]);
            client.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = builder.Configuration["ClusterId"] ?? throw new InvalidOperationException("ClusterId is required.");
                options.ServiceId = builder.Configuration["ServiceId"] ?? throw new InvalidOperationException("ServiceId is required.");
            });
        });
        var host = builder.Build();
        var activity = StartScriptActivity();
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return new DigitalBrainConnection(host, host.Services.GetRequiredService<IDigitalBrain>(), builder.Configuration, activity, relay);
        }
        catch
        {
            activity?.Dispose();
            host.Dispose();
            if (relay is not null) { await relay.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    // Orleans static clustering needs IP endpoints; containers reach the silo by host name.
    public static IReadOnlyList<Uri> GatewayEndpoints(string gateways)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateways);
        return gateways.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(gateway => new Uri(gateway))
            .Select(gateway => gateway.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                ? gateway
                : new UriBuilder(gateway) { Host = ResolveIPv4(gateway.Host).ToString() }.Uri)
            .ToArray();
    }

    private static IPAddress ResolveIPv4(string host)
        => Dns.GetHostAddresses(host).FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? throw new InvalidOperationException($"Gateway host '{host}' has no IPv4 address.");

    private static Activity? StartScriptActivity()
    {
        var traceParent = Environment.GetEnvironmentVariable("TRACEPARENT");
        if (traceParent is null || !ActivityContext.TryParse(traceParent, Environment.GetEnvironmentVariable("TRACESTATE"), out var parent))
        { return null; }
        return ScriptActivity.CreateActivity("csharp.run", ActivityKind.Consumer, parent)?.Start();
    }
}
