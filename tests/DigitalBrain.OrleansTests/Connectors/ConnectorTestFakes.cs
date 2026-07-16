using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Integrations;

internal sealed class FakeIntegrationConfigStore : IIntegrationConfigStore
{
    private readonly Dictionary<(string scope, string pack), Dictionary<string, string>> _data = [];

    public Task SetAsync(
        string scope,
        string pack,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _data[(scope, pack)] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetAsync(
        string scope,
        string pack,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _data.TryGetValue((scope, pack), out var values)
            ? Task.FromResult<IReadOnlyDictionary<string, string>>(values)
            : Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
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

    public bool TryUnprotect(string state, out NeuronId owner) =>
        _owners.TryGetValue(state, out owner!);
}

internal sealed class FakeSalesforceApiClientFactory : ISalesforceApiClientFactory
{
    public Task<ISalesforceApiClient> CreateAsync(
        NeuronScope scope,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ISalesforceApiClient>(new FakeSalesforceApiClient());
}

internal sealed class FakeSalesforceApiClient : ISalesforceApiClient
{
    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct) =>
        Task.FromResult(Array.Empty<string>());
}
