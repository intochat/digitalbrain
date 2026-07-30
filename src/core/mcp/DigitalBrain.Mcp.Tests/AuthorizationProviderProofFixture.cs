using System.Net;
using System.Net.Sockets;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Salesforce;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationProviderProofFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _methodLease = new(1, 1);
    private FakeMcpProviderHost? _provider;
    private FixtureCluster? _cluster;
    private int _uiPort;

    internal FakeMcpProviderHost Provider
        => _provider ?? throw new InvalidOperationException("Fake MCP provider is not started.");

    internal Uri UiBaseAddress { get; private set; } = new("http://127.0.0.1/");

    internal Uri EdgeCallbackAddress
        => new(UiBaseAddress, UiHttpContract.McpOAuthCallbackPath.TrimStart('/'));

    public async ValueTask InitializeAsync()
    {
        _provider = await FakeMcpProviderHost.StartAsync(
            IntegrationsFixture.SampleMessageId,
            IntegrationsFixture.SampleSubject,
            IntegrationsFixture.SampleSender,
            IntegrationsFixture.SampleBody,
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
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            _methodLease.Dispose();
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
        // Real Flutter.Http OAuth callback endpoint — the edge the browser lands on.
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
        brain.Configure(McpRuntimeHosting.AuthorizationModeKey, McpRuntimeHosting.EdgeMode);
        brain.Configure(McpRuntimeHosting.AuthorizationPreflightKey, "false");
        brain.Configure(McpRuntimeHosting.PublicSignInBaseKey, provider.BaseAddress.AbsoluteUri);
        brain.Configure("DigitalBrain:Google:Gmail:Endpoint", provider.McpEndpoint.AbsoluteUri);
        brain.Configure("DigitalBrain:Google:Gmail:ClientId", FakeMcpProviderHost.ClientId);
        brain.Configure("DigitalBrain:Google:Gmail:ClientSecret", FakeMcpProviderHost.ClientSecret);
        brain.Configure("DigitalBrain:Google:Gmail:RedirectUri", EdgeCallbackAddress.AbsoluteUri);
        brain.WithResponseTimeout(TimeSpan.FromMinutes(2));
    }

    private FixtureCluster Cluster()
        => _cluster
            ?? throw new InvalidOperationException("The authorization provider proof fixture has not been initialized.");

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
