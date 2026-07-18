using DigitalBrain.Os.Application;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Hosting.DigitalBrain;
using DigitalBrain.Hosting.Microsoft.Aspire;
using DigitalBrain.Protocol.Microsoft.Aspire;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using Orleans;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Threading;

namespace DigitalBrain.Mcp.Tools;

[McpServerToolType]
internal sealed class DigitalBrainTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // All cluster access is on-demand inside the tool methods.
    // No DI of cluster at startup. The Mcp web host (Kestrel + /mcp) starts with zero Orleans dependency.
    // Connect attempt (and any failure) only happens when a tool is actually invoked by an MCP client.
    // This prevents the resource from ever failing to start due to gateway timing.

    private static IHost? cachedOrleansHost;
    private static IClusterClient? cachedClusterClient;
    private static string? lastClusterId;
    private static int lastGatewayPort;

    private async Task<T> WithClusterAsync<T>(Func<DigitalBrainCluster, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        var clusterId = Environment.GetEnvironmentVariable("DIGITALBRAIN_CLUSTER_ID") ?? "digitalbrain-root";
        int gatewayPort = int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_GATEWAY_PORT"), out var gp) ? gp : 30000;
        var gatewayEndpoint = new IPEndPoint(IPAddress.Loopback, gatewayPort);

        if (cachedClusterClient == null || lastClusterId != clusterId || lastGatewayPort != gatewayPort)
        {
            cachedOrleansHost?.Dispose();
            cachedOrleansHost = Host.CreateApplicationBuilder()
                .UseOrleansClient(c =>
                {
                    c.UseDigitalBrainClient(clusterId, gatewayEndpoint);
                    c.Configure<Orleans.Configuration.ClientMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromSeconds(10));
                })
                .Build();
            await cachedOrleansHost.StartAsync(cancellationToken);
            cachedClusterClient = cachedOrleansHost.Services.GetRequiredService<IClusterClient>();
            lastClusterId = clusterId;
            lastGatewayPort = gatewayPort;
        }

        try
        {
            var cl = new DigitalBrainCluster(cachedClusterClient);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    return await work(cl, cancellationToken);
                }
                catch (Exception) when (attempt < 4)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * (1 << attempt)), cancellationToken);
                }
            }
            throw new InvalidOperationException("cluster work failed after retries");
        }
        catch (Exception ex)
        {
            cachedOrleansHost = null;
            cachedClusterClient = null;
            lastClusterId = null;
            return (T)(object)JsonSerializer.Serialize(new
            {
                error = "digitalbrain cluster not reachable (gateway may still be starting, or connection refused)",
                detail = ex.Message,
                exceptionType = ex.GetType().FullName
            });
        }
    }

    [McpServerTool(Name = "list_installed_bundles")]
    [Description("Returns the list of bundle ids currently installed on the active DigitalBrain cluster (root or current world).")]
    public async Task<string> ListInstalledBundles(CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var bundles = await cl.Brain.ListInstalledBundlesAsync(c);
            return JsonSerializer.Serialize(bundles, JsonOptions);
        }, cancellationToken);
    }

    [McpServerTool(Name = "list_published_bundles")]
    [Description("Returns bundle ids that have been published to the marketplace (available for install by id or peer).")]
    public async Task<string> ListPublishedBundles(CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var bundles = await cl.Brain.ListPublishedBundlesAsync(c);
            return JsonSerializer.Serialize(bundles, JsonOptions);
        }, cancellationToken);
    }

    [McpServerTool(Name = "install_bundle")]
    [Description("Installs a bundle by id (from published marketplace or local pack). Triggers N+1 handler growth on the timeline for all new synapses declared by the bundle.")]
    public async Task<string> InstallBundle(
        [Description("The bundle id to install, e.g. 'google-auth' or a published capsule id.")] string bundleId,
        CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            await cl.Brain.InstallBundleAsync(bundleId, c);
            return JsonSerializer.Serialize(new { outcome = "installed", bundleId });
        }, cancellationToken);
    }

    [McpServerTool(Name = "uninstall_bundle")]
    [Description("Uninstalls a bundle by id. Reverses the handler count (N-1) and removes the declared synapses from dispatch.")]
    public async Task<string> UninstallBundle(
        [Description("The bundle id to remove.")] string bundleId,
        CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            await cl.Brain.UninstallBundleAsync(bundleId, c);
            return JsonSerializer.Serialize(new { outcome = "uninstalled", bundleId });
        }, cancellationToken);
    }

    [McpServerTool(Name = "get_recent_history")]
    [Description("Returns the most recent synapses fired on the timeline (causal chain for the brain). Useful for inspection before/after installs or actions.")]
    public async Task<string> GetRecentHistory(
        [Description("Max number of entries (default 20).")] int max = 20,
        CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var history = await cl.Brain.GetRecentHistoryAsync(max, c);
            var shaped = history.Select(s => new
            {
                type = s.GetType().Name,
                correlation = s.Metadata.CorrelationId,
                causation = s.Metadata.CausationId,
                firedAt = s.Timestamp
            });
            return JsonSerializer.Serialize(shaped, JsonOptions);
        }, cancellationToken);
    }

    [McpServerTool(Name = "list_active_neuron_types")]
    [Description("Lists the distinct neuron (grain) types currently active/known to the DigitalBrain (from manifest + runtime subscribers).")]
    public async Task<string> ListActiveNeuronTypes(CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var types = await cl.Brain.ListActiveNeuronTypesAsync(c);
            return JsonSerializer.Serialize(types, JsonOptions);
        }, cancellationToken);
    }

    [McpServerTool(Name = "restart_resource")]
    [Description("Restarts an Aspire resource by its resource name (e.g. 'kernel', 'kernel-example-world', 'google-auth-silo', 'flutter-web', 'flutter-windows'). Uses the IAspire grain so the action is journaled and observable on the timeline.")]
    public async Task<string> RestartResource(
        [Description("Exact Aspire resource name from the DistributedApplication model / dashboard.")] string resourceName,
        CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var aspire = cl.Client.GetGrain<IAspire>(Brain.WellKnownKey);
            await aspire.RestartResourceAsync(resourceName, c);
            return JsonSerializer.Serialize(new { outcome = "restart_requested", resourceName });
        }, cancellationToken);
    }

    [McpServerTool(Name = "get_dashboard_url")]
    [Description("Returns the current Aspire dashboard URL (with login token when available) for the active cluster.")]
    public async Task<string> GetDashboardUrl(CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var aspire = cl.Client.GetGrain<IAspire>(Brain.WellKnownKey);
            var url = await aspire.GetDashboardUrlAsync(c);
            return JsonSerializer.Serialize(new { dashboardUrl = url });
        }, cancellationToken);
    }

    [McpServerTool(Name = "get_orleans_dashboard_url")]
    [Description("Returns the Orleans cluster dashboard URL (grain/activation/silo viz) for the current digitalbrain cluster. Starts with the kernels and connects automatically (in-process on kernel http endpoint).")]
    public async Task<string> GetOrleansDashboardUrl(CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var aspire = cl.Client.GetGrain<IAspire>(Brain.WellKnownKey);
            var url = await aspire.GetOrleansDashboardUrlAsync(c);
            return JsonSerializer.Serialize(new { orleansDashboardUrl = url });
        }, cancellationToken);
    }

    [McpServerTool(Name = "list_subscribers")]
    [Description("List subscriber count for a synapse type name (N+1 / N-1 proof after install/uninstall of .ino bundles that declare triggers in their .ino).")]
    public async Task<string> ListSubscribers(
        [Description("Synapse type name, e.g. BundleInstalled or GmailLastSendersRequest")] string synapseTypeName,
        CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            var subs = await cl.Brain.ListSubscribersAsync(synapseTypeName, c);
            return JsonSerializer.Serialize(new { type = synapseTypeName, count = subs.Count });
        }, cancellationToken);
    }

    [McpServerTool(Name = "fire_demo")]
    [Description("Headless trigger for DEMO: sends ClientTap 'Demo' to cause server to emit UiSurface('demo-executed') with 'Demo Executed' card and telemetry. Use for CI/test without browser/flutter.")]
    public async Task<string> FireDemo(CancellationToken cancellationToken = default)
    {
        return await WithClusterAsync(async (cl, c) =>
        {
            await cl.Brain.SendAsync(new ClientTap("demo", "{\"Type\":\"Demo\"}"), c);
            return JsonSerializer.Serialize(new { outcome = "demo_fired", via = "ClientTap" });
        }, cancellationToken);
    }
}
