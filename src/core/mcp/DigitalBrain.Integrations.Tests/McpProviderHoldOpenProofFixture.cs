using System.Net;
using System.Net.Sockets;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Http;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class McpProviderHoldOpenProofFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _methodLease = new(1, 1);
    private readonly ScriptedChatClient _plannerChat = new();
    private FakeMcpProviderHost? _provider;
    private FixtureCluster? _cluster;
    private int _uiPort;

    internal ScriptedChatClient PlannerChat => _plannerChat;

    internal FakeMcpProviderHost Provider
        => _provider ?? throw new InvalidOperationException("Fake MCP provider is not started.");

    internal Uri UiBaseAddress { get; private set; } = new("http://127.0.0.1/");

    internal Uri AppCallbackAddress
        => new(UiBaseAddress, FlutterHttpContract.McpOAuthCallbackPath.TrimStart('/'));

    public async ValueTask InitializeAsync()
    {
        _provider = await FakeMcpProviderHost.StartForSalesforceAsync(
            IntegrationsFixture.SampleAccountId,
            IntegrationsFixture.SampleEnrichmentDescription,
            CancellationToken.None);

        _uiPort = FreePort();
        UiBaseAddress = new Uri($"http://127.0.0.1:{_uiPort}/");

        var brain = new DigitalBrainTestBuilder();
        Configure(brain);
        _cluster = await FixtureCluster.StartAsync(brain.Seal());
    }

    public async Task<TestBrain> CreateBrainAsync(CancellationToken cancellationToken = default)
    {
        await _methodLease.WaitAsync(cancellationToken);
        try
        {
            var scope = $"test-{Guid.NewGuid():N}";
            var cluster = Cluster();
            var diagnostics = new BrainTestDiagnostics(scope);
            var method = await cluster.PrepareMethodAsync(scope, diagnostics);
            return new TestBrain(
                cluster,
                scope,
                method.Clock,
                diagnostics,
                cluster.Edges,
                method.EdgeGeneration,
                () => _methodLease.Release());
        }
        catch
        {
            _methodLease.Release();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Interlocked.Exchange(ref _cluster, null) is { } cluster)
            {
                using var disposeHang = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    await cluster.DisposeAsync().AsTask().WaitAsync(disposeHang.Token);
                }
                catch (OperationCanceledException) when (disposeHang.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "McpProviderHoldOpenProofFixture cluster dispose exceeded 20s — hold-open Task still parked.");
                }
            }
        }
        finally
        {
            _methodLease.Dispose();
            _plannerChat.Dispose();
            if (_provider is not null)
            {
                await _provider.DisposeAsync();
                _provider = null;
            }
        }
    }

    internal async Task<WebApplication> StartUiEdgeAsync(TestBrain test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(UiBaseAddress.AbsoluteUri);
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(test.Client);
        builder.Services.AddSingleton<IGrainFactory>(test.Cluster.Client);

        var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapMcpOAuthCallback();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private void Configure(DigitalBrainTestBuilder brain)
    {
        var provider = Provider;

        brain.AddModule<GoogleModule>();
        brain.AddModule<SalesforceModule>();
        brain.AddModule<EnrichmentModule>();
        brain.AddModule<FlutterModule>();
        brain.AddModule<IntegrationsHarnessModule>();
        // Real HttpMcpClientSessionFactory — do not ConfigureMcpEdge / scripted factory.
        // Leave PublicSignInBase unset so the rail does not preflight-park; SDK hold-open OAuth
        // journals AuthorizationRequired with the provider authorize URL.
        // Salesforce hard-codes its production MCP endpoint; rewrite only the open target for this proof.
        brain.ConfigureServiceEdge(
            services =>
            {
                services.AddSingleton<IChatClient>(_plannerChat);
                services.RemoveAll<IMcpClientSessionFactory>();
                services.AddSingleton<IMcpClientSessionFactory>(sp =>
                {
                    var inner = ActivatorUtilities.CreateInstance<HttpMcpClientSessionFactory>(sp);
                    return new SalesforceEndpointRewriteSessionFactory(inner, provider.McpEndpoint);
                });
            },
            _plannerChat,
            static chat => chat.Reset());
        brain.Configure("DigitalBrain:Salesforce:ClientId", FakeMcpProviderHost.ClientId);
        brain.Configure("DigitalBrain:Salesforce:RedirectUri", AppCallbackAddress.AbsoluteUri);
        brain.WithResponseTimeout(TimeSpan.FromMinutes(2));
    }

    private FixtureCluster Cluster()
        => _cluster
            ?? throw new InvalidOperationException("The MCP hold-open provider proof fixture has not been initialized.");

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class SalesforceEndpointRewriteSessionFactory(
        IMcpClientSessionFactory inner,
        Uri endpoint) : IMcpClientSessionFactory
    {
        public ValueTask<McpClient> OpenAsync(
            McpServerDefinition server,
            IDurableValue<byte[]> tokenState,
            Func<ValueTask> commit,
            string durableIdentity,
            CancellationToken cancellationToken,
            McpAuthorizationAmbientState? ambient = null)
        {
            ArgumentNullException.ThrowIfNull(server);
            if (string.Equals(server.Key, IntegrationsFixture.SalesforceServerKey, StringComparison.Ordinal))
            {
                server = server.WithEndpoint(endpoint);
            }

            return inner.OpenAsync(server, tokenState, commit, durableIdentity, cancellationToken, ambient);
        }
    }
}
