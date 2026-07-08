using System.Threading;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Salesforce;
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
    public async Task ValidateConfigAsync_ReturnsStatus()
    {
        var connector = CreateConnector();
        var status = await connector.ValidateConfigAsync(userScope: "user:test");

        // Real providers (Google/Salesforce) check RequiredConfigKeys from store and return !IsValid + MissingKey when absent.
        // Dummy always valid. Provider contract subclasses will assert the missing case with fakes.
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

    public Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectorConfigStatus(true));

    public Task<AuthChallenge> BeginAuthAsync(NeuronId user, string? clientIdHint = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthChallenge("https://example.com/auth"));

    public Task<AuthResult> CompleteAuthAsync(OAuthCallback callback, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthResult(true));

    public Task<ConnectionHealth> TestConnectionAsync(NeuronId user, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectionHealth(true, "dummy healthy"));
}

/// Concrete using dummy for base contract validation in this slice.
public class DummyIConnectorContractTests : IConnectorContractTests<DummyConnector>
{
    protected override DummyConnector CreateConnector(NeuronId? user = null) => new DummyConnector();
}

// Fakes for real provider contract tests (in-mem store + stub factory; no network for auth path).
internal sealed class FakePackConfigStore : IPackConfigStore
{
    private readonly Dictionary<(string scope, string pack), Dictionary<string, string>> _data = [];

    public Task SetAsync(string scope, string pack, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _data[(scope, pack)] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetAsync(string scope, string pack, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _data.TryGetValue((scope, pack), out var d)
            ? Task.FromResult<IReadOnlyDictionary<string, string>>(d)
            : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}

internal sealed class FakeSalesforceApiClientFactory : ISalesforceApiClientFactory
{
    public Task<ISalesforceApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult<ISalesforceApiClient>(new FakeSalesforceApiClient());
}

internal sealed class FakeSalesforceApiClient : ISalesforceApiClient
{
    public Task<string[]> QueryAsync(string soql, CancellationToken ct = default) => throw new NotImplementedException("Auth path does not call; TestConnection does.");
    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct = default) => Task.FromResult(Array.Empty<string>());
}

internal sealed class FakeGoogleApiClientFactory // placeholder if needed for future Google TestConnection extension
{
}

// Provider contract tests exercising real impls + fakes (Validate missing/valid, Begin challenge, health stub).
public class SalesforceConnectorContractTests : IConnectorContractTests<SalesforceConnector>
{
    protected override SalesforceConnector CreateConnector(NeuronId? user = null)
    {
        var store = new FakePackConfigStore();
        var factory = new FakeSalesforceApiClientFactory();
        return new SalesforceConnector(factory, store);
    }

    [Fact]
    public async Task Validate_MissingKeys_ReturnsInvalid()
    {
        var connector = CreateConnector();
        var status = await connector.ValidateConfigAsync();
        Assert.NotNull(status);
        Assert.False(status.IsValid);
        Assert.NotNull(status.MissingKey);
    }
}

public class GoogleConnectorContractTests : IConnectorContractTests<GoogleConnector>
{
    protected override GoogleConnector CreateConnector(NeuronId? user = null)
    {
        var store = new FakePackConfigStore();
        return new GoogleConnector(store);
    }

    [Fact]
    public async Task Validate_MissingKeys_ReturnsInvalid()
    {
        var connector = CreateConnector();
        var status = await connector.ValidateConfigAsync();
        Assert.NotNull(status);
        Assert.False(status.IsValid);
        Assert.NotNull(status.MissingKey);
    }
}
