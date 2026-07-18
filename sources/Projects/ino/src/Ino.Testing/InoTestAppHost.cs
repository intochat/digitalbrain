using System.Net;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Testing;

/// <summary>
/// Boots the real Aspire AppHost via DistributedApplicationTestingBuilder.
/// Generic over TAppHost so consumers parameterize per test project.
/// Each fixture instance gets a unique installed.json path so test flows
/// don't collide.
/// </summary>
public class InoTestAppHost<TAppHost> : IAsyncLifetime
    where TAppHost : class
{
    public DistributedApplication App { get; protected set; } = null!;
    public string InstalledJsonPath { get; private set; } = null!;
    public string MarketplaceFeedPath { get; private set; } = null!;

    public virtual async ValueTask InitializeAsync()
    {
        InstalledJsonPath = Path.Combine(Path.GetTempPath(), $"ino-installed-{Guid.NewGuid()}.json");
        MarketplaceFeedPath = Path.Combine(Path.GetTempPath(), $"ino-marketplace-{Guid.NewGuid()}.json");
        Environment.SetEnvironmentVariable("INO_INSTALLED_JSON_PATH", InstalledJsonPath);
        Environment.SetEnvironmentVariable("INO_MARKETPLACE_JSON_PATH", MarketplaceFeedPath);

        // Opts silo-side AddInoChatClients out of constructing the production xAI
        // factory. Test fixtures register their own IChatClientFactory (see
        // BddMockChatClientFactory in Task 28). Child silo processes inherit this
        // env var from the parent test process.
        Environment.SetEnvironmentVariable("INO_TEST_MODE", "true");

        // Per-fixture isolation: silos use UseLocalhostClustering with hard-coded
        // ports for `aspire run` (so the dev dashboard URLs stay stable). Tests
        // would race those ports cross-fixture / cross-assembly — Windows holds
        // sockets in TIME_WAIT for ~30s after a process exits, and a back-to-back
        // assembly run hits "address already in use". Fix: pick six free ports
        // up front, plus a unique clusterId, and override via env vars that
        // InoOrleansEndpoints reads. Aspire propagates these to every child
        // silo process automatically.
        var allocator = new EphemeralPortAllocator();
        var kernelSiloPort = allocator.Reserve();
        var kernelGatewayPort = allocator.Reserve();
        var identitySiloPort = allocator.Reserve();
        var identityGatewayPort = allocator.Reserve();
        var travelSiloPort = allocator.Reserve();
        var travelGatewayPort = allocator.Reserve();
        var taxiSiloPort = allocator.Reserve();
        var taxiGatewayPort = allocator.Reserve();
        var clusterId = $"ino-test-{Guid.NewGuid():N}".Substring(0, 24);

        var travelDomain = DomainId.From("Ino.Domains.Travel");
        var taxiDomain = DomainId.From("Ino.Domains.Taxi");

        Environment.SetEnvironmentVariable(InoOrleansEndpoints.KernelSiloPortEnv, kernelSiloPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.KernelGatewayPortEnv, kernelGatewayPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.IdentitySiloPortEnv, identitySiloPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.IdentityGatewayPortEnv, identityGatewayPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.DomainSiloPortEnvFor(travelDomain), travelSiloPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.DomainGatewayPortEnvFor(travelDomain), travelGatewayPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.DomainSiloPortEnvFor(taxiDomain), taxiSiloPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.DomainGatewayPortEnvFor(taxiDomain), taxiGatewayPort.ToString());
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.ClusterIdEnv, clusterId);
        Environment.SetEnvironmentVariable(InoOrleansEndpoints.ServiceIdEnv, clusterId);

        // Now release the listeners so the silos can grab them. There's a
        // narrow race window between Stop() and the silo's Bind(), but on
        // localhost it's small enough in practice that retries aren't worth
        // the complexity.
        allocator.ReleaseAll();

        _ownedPortEnvVars = new[]
        {
            InoOrleansEndpoints.KernelSiloPortEnv, InoOrleansEndpoints.KernelGatewayPortEnv,
            InoOrleansEndpoints.IdentitySiloPortEnv, InoOrleansEndpoints.IdentityGatewayPortEnv,
            InoOrleansEndpoints.DomainSiloPortEnvFor(travelDomain), InoOrleansEndpoints.DomainGatewayPortEnvFor(travelDomain),
            InoOrleansEndpoints.DomainSiloPortEnvFor(taxiDomain), InoOrleansEndpoints.DomainGatewayPortEnvFor(taxiDomain),
            InoOrleansEndpoints.ClusterIdEnv, InoOrleansEndpoints.ServiceIdEnv,
        };

        WriteDefaultFeed(MarketplaceFeedPath);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<TAppHost>();

        // Stub every secret parameter the production AppHost declares. The
        // dashboard prompts for them on first run; in tests we just hand
        // each one a placeholder so silos don't sit in Waiting forever
        // resolving an unfilled ParameterResource. INO_TEST_MODE=true above
        // routes the LLM stack through BddMockChatClient anyway, so the
        // actual value never reaches the network.
        foreach (var p in builder.Resources.OfType<ParameterResource>())
            builder.Configuration[$"Parameters:{p.Name}"] = "test";

        App = await builder.BuildAsync();
        await App.StartAsync();

        await App.ResourceNotifications.WaitForResourceHealthyAsync("kernel");
        await App.ResourceNotifications.WaitForResourceHealthyAsync("identity");
        await App.ResourceNotifications.WaitForResourceHealthyAsync("travel");
        await App.ResourceNotifications.WaitForResourceHealthyAsync("taxi");
    }

    public HttpClient CreateKernelHttpClient() =>
        App.CreateHttpClient("kernel", "kernel-http");

    /// <summary>
    /// Pre-populates the marketplace feed with the two test-fixture bundles so
    /// positive install flows have something to install. Uninstalled by
    /// default — InstalledSet starts empty (installed.json does not exist).
    /// JSON is authored by hand to avoid dragging Ino.Aspire.Hosting into
    /// Ino.Testing's dependency tree.
    /// </summary>
    private static void WriteDefaultFeed(string feedPath)
    {
        const string json = """
            {
              "domains": [
                { "id": "Ino.Testing.Fixture.Alpha", "description": "alpha test fixture", "version": "1.0.0", "neurons": [] },
                { "id": "Ino.Testing.Fixture.Beta",  "description": "beta test fixture",  "version": "1.0.0", "neurons": [] }
              ]
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(feedPath)!);
        File.WriteAllText(feedPath, json);
    }

    string[] _ownedPortEnvVars = Array.Empty<string>();

    public virtual async ValueTask DisposeAsync()
    {
        try
        {
            await App.DisposeAsync();
        }
        finally
        {
            if (File.Exists(InstalledJsonPath)) File.Delete(InstalledJsonPath);
            if (File.Exists(MarketplaceFeedPath)) File.Delete(MarketplaceFeedPath);
            Environment.SetEnvironmentVariable("INO_INSTALLED_JSON_PATH", null);
            Environment.SetEnvironmentVariable("INO_MARKETPLACE_JSON_PATH", null);
            Environment.SetEnvironmentVariable("INO_TEST_MODE", null);
            foreach (var name in _ownedPortEnvVars)
                Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>
    /// Reserves free localhost TCP ports by binding listeners on port 0 (the
    /// kernel picks an ephemeral port) and holding the listeners until
    /// <see cref="ReleaseAll"/>. Holding all listeners until the very end
    /// guarantees no two reservations collide with each other within the
    /// fixture; once released, the silos race to bind before another
    /// process can grab the port — small but not zero.
    /// </summary>
    sealed class EphemeralPortAllocator
    {
        readonly List<TcpListener> _listeners = new();

        public int Reserve()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _listeners.Add(listener);
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void ReleaseAll()
        {
            foreach (var listener in _listeners) listener.Stop();
            _listeners.Clear();
        }
    }
}
