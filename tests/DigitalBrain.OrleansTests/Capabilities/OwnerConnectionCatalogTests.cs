extern alias McpProject;

using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DigitalBrainUiEndpoints = McpProject::DigitalBrain.Mcp.DigitalBrainUiEndpoints;
using IOwnerConnectionCatalogClient = McpProject::DigitalBrain.Mcp.IOwnerConnectionCatalogClient;
using GrpcGetConnectionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.GetConnectionRequest;
using GrpcConnectionHealthStatus = McpProject::DigitalBrain.V2.Ui.Grpc.ConnectionHealthStatus;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
using RuntimeAuthAssurance = DigitalBrain.Kernel.Contracts.Runtime.AuthAssurance;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class OwnerConnectionCatalogTests
{
    private static readonly BrainOwnerId Owner = new("owner-scope");

    [Fact]
    public async Task ReadAsync_projects_healthy_providers_with_unlocked_capability_ids()
    {
        var google = new StubConnector("google", "Google", configValid: true, healthy: true, healthDetail: "ok");
        var salesforce = new StubConnector("salesforce", "Salesforce CRM", configValid: true, healthy: true, healthDetail: "ok");
        var web = new StubConnector("web", "Web", configValid: true, healthy: true, healthDetail: "ok");
        var services = new ServiceCollection()
            .AddKeyedSingleton<IConnector>("google", google)
            .AddKeyedSingleton<IConnector>("salesforce", salesforce)
            .AddKeyedSingleton<IConnector>("web", web)
            .BuildServiceProvider();
        var catalog = new OwnerConnectionCatalog(
            services,
            new StaticCapabilityCatalog(
                Descriptor("assistant.answer", []),
                Descriptor("google.read", ["google"]),
                Descriptor("salesforce.read", ["salesforce"]),
                Descriptor("web.search.v1", ["web"])));

        var snapshots = await catalog.ReadAsync(Owner);

        Assert.Equal(["google", "salesforce", "web"], snapshots.Select(snapshot => snapshot.Provider));
        var googleSnapshot = Assert.Single(snapshots, snapshot => snapshot.Provider == "google");
        Assert.Equal(OwnerConnectionHealthStatus.Healthy, googleSnapshot.Health);
        Assert.Equal("Google", googleSnapshot.DisplayName);
        Assert.Equal("google", googleSnapshot.ConnectionId);
        Assert.Equal(["google.read"], googleSnapshot.UnlockedCapabilityIds);
        Assert.Equal("/oauth/start/google", googleSnapshot.ConnectPath);
        Assert.Equal(Owner.Value, google.LastOwner?.Value);
        Assert.Equal(OwnerConnectionHealthStatus.Healthy, Assert.Single(snapshots, snapshot => snapshot.Provider == "salesforce").Health);
        Assert.Equal(OwnerConnectionHealthStatus.Healthy, Assert.Single(snapshots, snapshot => snapshot.Provider == "web").Health);
        Assert.Equal(["web.search.v1"], Assert.Single(snapshots, snapshot => snapshot.Provider == "web").UnlockedCapabilityIds);
    }

    [Fact]
    public async Task ReadAsync_never_reports_healthy_when_probe_fails_or_throws()
    {
        var unhealthy = new StubConnector("google", "Google", configValid: true, healthy: false, healthDetail: "token expired");
        var throwing = new StubConnector("salesforce", "Salesforce CRM", configValid: true, healthy: true, throwOnProbe: true);
        var services = new ServiceCollection()
            .AddKeyedSingleton<IConnector>("google", unhealthy)
            .AddKeyedSingleton<IConnector>("salesforce", throwing)
            .BuildServiceProvider();
        var catalog = new OwnerConnectionCatalog(
            services,
            new StaticCapabilityCatalog(
                Descriptor("google.read", ["google"]),
                Descriptor("salesforce.read", ["salesforce"])));

        var snapshots = await catalog.ReadAsync(Owner);

        var googleSnapshot = Assert.Single(snapshots, snapshot => snapshot.Provider == "google");
        Assert.Equal(OwnerConnectionHealthStatus.NeedsReauth, googleSnapshot.Health);
        Assert.NotEqual(OwnerConnectionHealthStatus.Healthy, googleSnapshot.Health);
        Assert.Equal("token expired", googleSnapshot.HealthDetail);

        var salesforceSnapshot = Assert.Single(snapshots, snapshot => snapshot.Provider == "salesforce");
        Assert.Equal(OwnerConnectionHealthStatus.Disconnected, salesforceSnapshot.Health);
        Assert.NotEqual(OwnerConnectionHealthStatus.Healthy, salesforceSnapshot.Health);
        Assert.Equal("Connection probe failed.", salesforceSnapshot.HealthDetail);
    }

    [Fact]
    public async Task ReadAsync_always_includes_known_providers_even_without_connectors()
    {
        var catalog = new OwnerConnectionCatalog(
            new ServiceCollection().BuildServiceProvider(),
            new StaticCapabilityCatalog(Descriptor("assistant.answer", [])));

        var snapshots = await catalog.ReadAsync(Owner);

        Assert.Equal(["google", "salesforce", "web"], snapshots.Select(snapshot => snapshot.Provider));
        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(OwnerConnectionHealthStatus.Disconnected, snapshot.Health);
            Assert.Equal($"/oauth/start/{snapshot.Provider}", snapshot.ConnectPath);
            Assert.Empty(snapshot.UnlockedCapabilityIds);
        });
    }

    [Fact]
    public async Task ReadAsync_maps_invalid_config_to_misconfigured()
    {
        var connector = new StubConnector(
            "google",
            "Google",
            configValid: false,
            healthy: true,
            missingKey: "client_id",
            configMessage: "Missing client_id");
        var services = new ServiceCollection()
            .AddKeyedSingleton<IConnector>("google", connector)
            .BuildServiceProvider();
        var catalog = new OwnerConnectionCatalog(
            services,
            new StaticCapabilityCatalog(Descriptor("google.read", ["google"])));

        var snapshot = Assert.Single(await catalog.ReadAsync(Owner), item => item.Provider == "google");

        Assert.Equal(OwnerConnectionHealthStatus.Misconfigured, snapshot.Health);
        Assert.Equal("Missing client_id", snapshot.HealthDetail);
        Assert.Equal("/oauth/start/google", snapshot.ConnectPath);
        Assert.False(connector.ProbeCalled);
    }

    [Fact]
    public async Task GetConnection_unknown_id_returns_not_found()
    {
        var client = new StaticConnectionCatalogClient([
            new OwnerConnectionSnapshot(
                "google",
                "google",
                "Google",
                OwnerConnectionHealthStatus.Healthy,
                "ok",
                ["google.read"],
                "/oauth/start/google")
        ]);
        var endpoints = new DigitalBrainUiEndpoints(
            authoring: null!,
            suggestions: null!,
            logger: NullLogger<DigitalBrainUiEndpoints>.Instance,
            connections: client);
        var context = new RuntimeRequestContext(
            Owner,
            new ActorId("actor-scope"),
            new SessionId("session-scope"),
            RuntimeAuthAssurance.Password,
            "correlation-scope",
            null,
            new HashSet<string>(StringComparer.Ordinal) { "brain.read" });

        var found = await endpoints.GetConnectionAsync(
            context,
            new GrpcGetConnectionRequest { ConnectionId = "google" },
            CancellationToken.None);
        Assert.Equal("google", found.Connection.ConnectionId);
        Assert.Equal(GrpcConnectionHealthStatus.Healthy, found.Connection.Health);

        var missing = await Assert.ThrowsAsync<RpcException>(() => endpoints.GetConnectionAsync(
            context,
            new GrpcGetConnectionRequest { ConnectionId = "missing" },
            CancellationToken.None));
        Assert.Equal(StatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ReadAsync_returns_detached_snapshots_for_subsequent_reads()
    {
        var google = new StubConnector("google", "Google", configValid: true, healthy: true, healthDetail: "ok");
        var services = new ServiceCollection()
            .AddKeyedSingleton<IConnector>("google", google)
            .BuildServiceProvider();
        var catalog = new OwnerConnectionCatalog(
            services,
            new StaticCapabilityCatalog(Descriptor("google.read", ["google"])));

        var first = await catalog.ReadAsync(Owner);
        var googleIndex = Array.FindIndex(first, snapshot => snapshot.Provider == "google");
        Assert.True(googleIndex >= 0);
        first[googleIndex] = first[googleIndex] with { HealthDetail = "tampered" };
        var second = await catalog.ReadAsync(Owner);

        Assert.Equal("ok", Assert.Single(second, snapshot => snapshot.Provider == "google").HealthDetail);
        Assert.Equal(Owner.Value, google.LastOwner?.Value);
    }

    private static CapabilityDescriptor Descriptor(string id, string[] connections) => new(
        id,
        1,
        id,
        id,
        [],
        [],
        connections,
        connections.Length == 0 ? CapabilityOrigin.Platform : CapabilityOrigin.Integration,
        CapabilityOperationKind.Query,
        true);

    private sealed class StaticCapabilityCatalog(params CapabilityDescriptor[] descriptors) : ICapabilityCatalog
    {
        public IReadOnlyList<CapabilityDescriptor> Snapshot() => descriptors;
    }

    private sealed class StaticConnectionCatalogClient(IReadOnlyList<OwnerConnectionSnapshot> snapshots)
        : IOwnerConnectionCatalogClient
    {
        public Task<IReadOnlyList<OwnerConnectionSnapshot>> ReadAsync(
            BrainOwnerId ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshots);
    }

    private sealed class StubConnector(
        string provider,
        string displayName,
        bool configValid,
        bool healthy,
        string? healthDetail = null,
        string? missingKey = null,
        string? configMessage = null,
        bool throwOnProbe = false) : IConnector
    {
        public NeuronId? LastOwner { get; private set; }
        public bool ProbeCalled { get; private set; }
        public ConnectorDescriptor Descriptor { get; } = new(provider, displayName, [], []);

        public Task<ConnectionHealth> TestConnectionAsync(
            NeuronId user,
            CancellationToken cancellationToken = default)
        {
            ProbeCalled = true;
            LastOwner = user;
            if (throwOnProbe)
                throw new InvalidOperationException("probe exploded");
            return Task.FromResult(new ConnectionHealth(healthy, healthDetail, DateTimeOffset.UtcNow));
        }

        public Task<ConnectorConfigStatus> ValidateConfigAsync(
            string? userScope = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(configValid
                ? new ConnectorConfigStatus(true)
                : new ConnectorConfigStatus(false, MissingKey: missingKey, Message: configMessage));

        public Task<AuthChallenge> BeginAuthAsync(
            NeuronId user,
            string? clientIdHint = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthResult> CompleteAuthAsync(
            OAuthCallback callback,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
