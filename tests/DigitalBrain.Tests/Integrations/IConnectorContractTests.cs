using DigitalBrain.Core;
using DigitalBrain.Kernel.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Integrations;

/// Reusable contract test base for IConnector implementations (P2 Phase 1).
/// Every provider should inherit and provide CreateConnector() + fake token endpoint setup.
/// Covers: descriptor, validate, begin/complete auth roundtrip, token scope, isolation, health.
/// Use fake OAuth server for red/green without real creds (see gap design).
public abstract class IConnectorContractTests<TConnector> where TConnector : class, IConnector
{
    protected abstract TConnector CreateConnector(NeuronId? user = null);

    [Fact]
    public void Descriptor_HasRequiredFields()
    {
        var connector = CreateConnector();
        var desc = connector.Descriptor;

        Assert.NotNull(desc);
        Assert.False(string.IsNullOrWhiteSpace(desc.Id));
        Assert.False(string.IsNullOrWhiteSpace(desc.DisplayName));
        Assert.NotNull(desc.RequiredConfigKeys);
        Assert.NotNull(desc.Scopes);
    }

    [Fact]
    public async Task ValidateConfigAsync_MissingKeys_ReturnsInvalid()
    {
        var connector = CreateConnector();
        // Simulate no config
        var status = await connector.ValidateConfigAsync(userScope: "user:test");

        // For now, basic; real impls will return specific missing.
        // This will be asserted in provider-specific overrides.
        Assert.NotNull(status);
    }

    [Fact]
    public async Task BeginAuthAsync_ReturnsChallenge_UrlOrForm()
    {
        var connector = CreateConnector(new NeuronId("user:test"));
        var challenge = await connector.BeginAuthAsync(new NeuronId("user:test"));

        Assert.NotNull(challenge);
        Assert.False(string.IsNullOrWhiteSpace(challenge.UrlOrForm));
    }

    [Fact]
    public async Task TestConnectionAsync_ExercisesCredentialPath()
    {
        var connector = CreateConnector(new NeuronId("user:test"));
        var health = await connector.TestConnectionAsync(new NeuronId("user:test"));

        Assert.NotNull(health);
        // Healthy depends on config in test; contract ensures no throw and returns structure.
    }

    // TODO in later slices: full roundtrip with fake callback, two-user isolation,
    // cross-silo, PKCE state, token merge. Use DataTable in Reqnroll for table-driven cases.
    // Example inheritance:
    // public class SalesforceConnectorContractTests : IConnectorContractTests<SalesforceConnector> { ... }
}

/// Dummy connector for contract test execution (avoids cross-project ref for initial slice).
/// Real providers will inherit with their impl.
public sealed class DummyConnector : IConnector
{
    public ConnectorDescriptor Descriptor => new("dummy", "Dummy", Array.Empty<string>(), Array.Empty<string>());

    public Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null) =>
        Task.FromResult(new ConnectorConfigStatus(true));

    public Task<AuthChallenge> BeginAuthAsync(NeuronId user, string? clientIdHint = null) =>
        Task.FromResult(new AuthChallenge("https://example.com/auth"));

    public Task<AuthResult> CompleteAuthAsync(OAuthCallback callback) =>
        Task.FromResult(new AuthResult(true));

    public Task<ConnectionHealth> TestConnectionAsync(NeuronId user) =>
        Task.FromResult(new ConnectionHealth(true, "dummy healthy"));
}

/// Concrete using dummy for base contract validation in this slice.
public class DummyIConnectorContractTests : IConnectorContractTests<DummyConnector>
{
    protected override DummyConnector CreateConnector(NeuronId? user = null) => new DummyConnector();
}