using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Integrations;
using DigitalBrain.Kernel;
using DigitalBrain.Sdk.Integrations;
using Orleans.Concurrency;

namespace DigitalBrain.Platform.Integrations.Accounts;

[GenerateSerializer, Alias("integrations.script-accounts")]
public sealed class ScriptAccountsState
{
    [Id(0)] public AccountRef[] Pending { get; set; } = [];
}

// Script-facing accounts. Grain type "integrations.accounts" does not collide with the credential registry ("connections").
[GrainType("integrations.accounts")]
internal sealed class ScriptAccountsNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ScriptAccountsState> store,
    IReadOnlyList<IntegrationDefinition> definitions)
    : Neuron<ScriptAccountsState>(store), IIntegrationAccounts
{
    [ReadOnly]
    public Task<IntegrationKind[]> ListKinds()
        => Task.FromResult(definitions.Select(definition => new IntegrationKind(definition.Id, definition.DisplayName)).ToArray());

    [ReadOnly]
    public Task<AccountRef[]> ListPending() => Task.FromResult<AccountRef[]>([.. Snapshot.Pending]);

    public async Task<AccountRef> Request(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var id = kind.Trim();
        if (!definitions.Any(definition => definition.Id == id))
        {
            throw new ArgumentException($"Integration '{kind}' is not available.");
        }

        var account = new AccountRef(id, Guid.NewGuid().ToString("n"));
        var next = Snapshot;
        next.Pending = [.. next.Pending, account];
        await Save(next, new AccountRequested(account.Kind, account.Id));
        return account;
    }
}
