using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class OwnerCapabilityCatalogTests
{
    private static readonly BrainOwnerId Owner = new("owner-scope");
    private static readonly ActorId Actor = new("actor-scope");
    private static readonly FeatureInstallationId Installation = new("inbox-brief");
    private static readonly ReleaseDigest Release = new(new string('a', 64));

    [Fact]
    public async Task ReadAsync_composes_only_healthy_owner_actor_scoped_capabilities()
    {
        var source = new StaticProjectionSource(
            Projection(),
            Projection() with { ActorId = new ActorId("another-actor"), InstallationId = new FeatureInstallationId("other") });
        var health = new MutableConnectionHealth("google");
        var catalog = new OwnerCapabilityCatalog(StaticCatalog(), source, health);

        var snapshot = await catalog.ReadAsync(Owner, Actor);

        Assert.Contains(snapshot.Entries, entry => entry.Descriptor.Id == "assistant.answer");
        Assert.Contains(snapshot.Entries, entry => entry.Descriptor.Id == "google.read");
        Assert.DoesNotContain(snapshot.Entries, entry => entry.Descriptor.Id == "salesforce.read");
        var feature = Assert.Single(snapshot.Entries, entry => entry.Feature is not null);
        Assert.Equal(Owner, feature.Feature!.OwnerId);
        Assert.Equal(Actor, feature.Feature.ActorId);
        Assert.Equal(Installation, feature.Feature.InstallationId);
        Assert.Equal(Release, feature.Feature.Release);
        Assert.Equal(["google"], feature.Descriptor.RequiredConnections);
        Assert.Contains("Summarize an inbox safely", feature.Descriptor.Description, StringComparison.Ordinal);
        Assert.Contains("when the owner asks for a brief", feature.Descriptor.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(Owner.Value, feature.Descriptor.Id, StringComparison.Ordinal);
        Assert.DoesNotContain(Installation.Value, feature.Descriptor.Id, StringComparison.Ordinal);
        Assert.DoesNotContain(Release.Value, feature.Descriptor.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_reflects_connection_loss_on_the_next_snapshot()
    {
        var health = new MutableConnectionHealth("google");
        var catalog = new OwnerCapabilityCatalog(StaticCatalog(), new StaticProjectionSource(Projection()), health);

        var connected = await catalog.ReadAsync(Owner, Actor);
        health.Replace();
        var disconnected = await catalog.ReadAsync(Owner, Actor);

        Assert.Contains(connected.Entries, entry => entry.Descriptor.Id == "google.read");
        Assert.Contains(connected.Entries, entry => entry.Feature is not null);
        Assert.DoesNotContain(disconnected.Entries, entry => entry.Descriptor.Id == "google.read");
        Assert.DoesNotContain(disconnected.Entries, entry => entry.Feature is not null);
        Assert.Contains(disconnected.Entries, entry => entry.Descriptor.Id == "assistant.answer");
    }

    [Fact]
    public async Task ReadAsync_excludes_a_feature_with_a_missing_provider_connection()
    {
        var projection = Projection() with
        {
            Grants = [new FeatureGrantSpec("google.gmail.read", 1, null, "{}", "google")]
        };
        var catalog = new OwnerCapabilityCatalog(
            StaticCatalog(),
            new StaticProjectionSource(projection),
            new MutableConnectionHealth("google"));

        var snapshot = await catalog.ReadAsync(Owner, Actor);

        Assert.DoesNotContain(snapshot.Entries, entry => entry.Feature is not null);
    }

    [Fact]
    public async Task ReadAsync_excludes_a_feature_bound_to_a_nonexistent_connection_instance()
    {
        var projection = Projection() with
        {
            Grants =
            [
                new FeatureGrantSpec(
                    "google.gmail.read",
                    1,
                    new ProviderConnectionId("google-primary"),
                    "{}",
                    "google")
            ]
        };
        var catalog = new OwnerCapabilityCatalog(
            StaticCatalog(),
            new StaticProjectionSource(projection),
            new MutableConnectionHealth("google"));

        var snapshot = await catalog.ReadAsync(Owner, Actor);

        Assert.DoesNotContain(snapshot.Entries, entry => entry.Feature is not null);
    }

    [Fact]
    public async Task ReadAsync_does_not_turn_an_unavailable_owner_projection_into_an_empty_catalog()
    {
        var catalog = new OwnerCapabilityCatalog(
            StaticCatalog(),
            new ThrowingProjectionSource(),
            new MutableConnectionHealth("google"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ReadAsync(Owner, Actor));
    }

    [Fact]
    public async Task Connection_health_uses_the_owner_keyed_connector_and_fails_closed()
    {
        var google = new StubConnector("google", healthy: true);
        var salesforce = new StubConnector("salesforce", healthy: false);
        var services = new ServiceCollection()
            .AddKeyedSingleton<IConnector>("google", google)
            .AddKeyedSingleton<IConnector>("salesforce", salesforce)
            .BuildServiceProvider();
        var health = new OwnerConnectionHealth(services);

        var exactGoogle = new CapabilityConnectionBinding("google", new ProviderConnectionId("google"));
        var wrongGoogle = new CapabilityConnectionBinding("google", new ProviderConnectionId("google-primary"));
        var healthy = await health.ReadHealthyAsync(
            Owner,
            [
                new CapabilityConnectionBinding("google", null),
                exactGoogle,
                wrongGoogle,
                new CapabilityConnectionBinding("salesforce", null),
                new CapabilityConnectionBinding("missing", null)
            ]);

        Assert.Contains(new CapabilityConnectionBinding("google", null), healthy);
        Assert.Contains(exactGoogle, healthy);
        Assert.DoesNotContain(wrongGoogle, healthy);
        Assert.Equal(Owner.Value, google.LastOwner?.Value);
        Assert.Equal(Owner.Value, salesforce.LastOwner?.Value);
    }

    private static FeatureCapabilityProjection Projection() => new(
        Owner,
        Installation,
        Actor,
        Release,
        new GrantRevision(3),
        "Summarize an inbox safely",
        [new FeatureScenario("scenario-1", "Inbox brief", "mail exists", "when the owner asks for a brief", "then return a summary")],
        [new FeatureGrantSpec("google.gmail.read", 1, new ProviderConnectionId("google"), "{}", "google")],
        "manual",
        5,
        "authority-digest",
        "access-digest");

    private static ICapabilityCatalog StaticCatalog() => new StaticCapabilityCatalog(
        Descriptor("assistant.answer", []),
        Descriptor("google.read", ["google"]),
        Descriptor("salesforce.read", ["salesforce"]));

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

    private sealed class StaticProjectionSource(params FeatureCapabilityProjection[] projections)
        : IFeatureCapabilityProjectionSource
    {
        public Task<IReadOnlyList<FeatureCapabilityProjection>> ReadAsync(
            BrainOwnerId ownerId,
            ActorId actorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FeatureCapabilityProjection>>(projections);
    }

    private sealed class ThrowingProjectionSource : IFeatureCapabilityProjectionSource
    {
        public Task<IReadOnlyList<FeatureCapabilityProjection>> ReadAsync(
            BrainOwnerId ownerId,
            ActorId actorId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<FeatureCapabilityProjection>>(
                new InvalidOperationException("owner catalog unavailable"));
    }

    private sealed class MutableConnectionHealth(params string[] healthy) : IOwnerConnectionHealth
    {
        private HashSet<string> _healthy = healthy.ToHashSet(StringComparer.Ordinal);

        public void Replace(params string[] providers) =>
            _healthy = providers.ToHashSet(StringComparer.Ordinal);

        public Task<IReadOnlySet<CapabilityConnectionBinding>> ReadHealthyAsync(
            BrainOwnerId ownerId,
            IReadOnlyCollection<CapabilityConnectionBinding> connections,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<CapabilityConnectionBinding>>(connections
                .Where(connection => _healthy.Contains(connection.Provider))
                .Where(static connection => connection.ConnectionId is null ||
                    string.Equals(connection.ConnectionId.Value.Value, connection.Provider, StringComparison.Ordinal))
                .ToHashSet());
    }

    private sealed class StubConnector(string provider, bool healthy) : IConnector
    {
        public NeuronId? LastOwner { get; private set; }
        public ConnectorDescriptor Descriptor { get; } = new(provider, provider, [], []);

        public Task<ConnectionHealth> TestConnectionAsync(
            NeuronId user,
            CancellationToken cancellationToken = default)
        {
            LastOwner = user;
            return Task.FromResult(new ConnectionHealth(healthy));
        }

        public Task<ConnectorConfigStatus> ValidateConfigAsync(
            string? userScope = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
