using System.Threading;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Salesforce;
using Xunit;

namespace DigitalBrain.Tests.Integrations;

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

    }

}

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

public class DummyIConnectorContractTests : IConnectorContractTests<DummyConnector>
{
    protected override DummyConnector CreateConnector(NeuronId? user = null) => new DummyConnector();
}

internal sealed class FakeIntegrationConfigStore : IIntegrationConfigStore
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

internal sealed class FakeOAuthStateProtector : IOAuthStateProtector
{
    private readonly Dictionary<string, NeuronId> _owners = new(StringComparer.Ordinal);

    public string Protect(NeuronId owner)
    {
        var state = "opaque-" + Guid.NewGuid().ToString("N");
        _owners[state] = owner;
        return state;
    }

    public bool TryUnprotect(string state, out NeuronId owner) => _owners.TryGetValue(state, out owner!);
}

internal sealed class FakeSalesforceApiClient : ISalesforceApiClient
{
    public Task<string> GetCurrentUserProfileAsync(CancellationToken ct = default) => Task.FromResult("{}");
    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct = default) => Task.FromResult(Array.Empty<string>());
    public Task<string[]> ListContactsAsync(int maxResults, CancellationToken ct = default) => Task.FromResult(Array.Empty<string>());
    public Task<string> DescribeCrmAccessAsync(CancellationToken ct = default) => Task.FromResult("{}");
}

internal sealed class FakeGoogleApiClientFactory
{
}

public class SalesforceConnectorContractTests : IConnectorContractTests<SalesforceConnector>
{
    protected override SalesforceConnector CreateConnector(NeuronId? user = null)
    {
        var store = new FakeIntegrationConfigStore();
        var factory = new FakeSalesforceApiClientFactory();
        return new SalesforceConnector(factory, store, new FakeOAuthStateProtector());
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

    [Fact]
    public async Task Failed_health_probe_does_not_expose_provider_exception_details()
    {
        const string providerDetail = "provider response contained sensitive detail";
        var connector = new SalesforceConnector(
            new FailingSalesforceApiClientFactory(providerDetail),
            new FakeIntegrationConfigStore(),
            new FakeOAuthStateProtector());

        var health = await connector.TestConnectionAsync(new NeuronId("user:test"));

        Assert.False(health.Healthy);
        Assert.Equal("Salesforce connection probe failed.", health.Detail);
        Assert.DoesNotContain(providerDetail, health.Detail, StringComparison.Ordinal);
    }

    private sealed class FailingSalesforceApiClientFactory(string detail) : ISalesforceApiClientFactory
    {
        public Task<ISalesforceApiClient> CreateAsync(
            NeuronScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ISalesforceApiClient>(new InvalidOperationException(detail));
    }
}

public class GoogleConnectorContractTests : IConnectorContractTests<GoogleConnector>
{
    protected override GoogleConnector CreateConnector(NeuronId? user = null)
    {
        var store = new FakeIntegrationConfigStore();
        return new GoogleConnector(store, new FakeOAuthStateProtector());
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
