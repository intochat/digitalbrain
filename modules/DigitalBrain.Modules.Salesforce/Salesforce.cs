using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Salesforce;

internal sealed class Salesforce : Neuron, ISalesforce
{
    private const string MutationsName = "salesforce.mutations";
    private static readonly Uri Endpoint = new(
        "https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations");

    private readonly ISalesforceMcpAuthorization _authorization;
    private readonly ISalesforceMcpTransport _transport;
    private readonly IDurableDictionary<Guid, byte[]> _mutations;
    private readonly Serializer<MutationData> _states;

    public Salesforce(
        ISalesforceMcpAuthorization authorization,
        ISalesforceMcpTransport transport)
    {
        _authorization = authorization;
        _transport = transport;
        _mutations = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(
            MutationsName);
        _states = ServiceProvider.GetRequiredService<Serializer<MutationData>>();
    }

    public async Task<SalesforceAccountDescriptionMutation> ProposeAccountDescriptionAsync(
        CommandId commandId,
        string accountId,
        string description)
    {
        Validate(commandId, accountId, description);

        var fingerprint = Fingerprint(accountId, description);

        if (TryLoad(commandId, out var existing))
        {
            EnsureSame(existing, fingerprint);
            return Receipt(existing);
        }

        var proposed = new MutationData(
            commandId,
            accountId,
            description,
            fingerprint,
            MutationStatus.Proposed);
        await SaveAsync(proposed, add: true);

        proposed = proposed with { Status = MutationStatus.AwaitingApproval };
        await SaveAsync(proposed);

        return Receipt(proposed);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any failure after durable Invoking makes the external mutation outcome uncertain and must not escape into an automatic retry path.")]
    public async Task<SalesforceAccountDescriptionMutation> ApproveAccountDescriptionAsync(
        CommandId commandId,
        string fingerprint)
    {
        if (commandId == default)
        {
            throw new ArgumentException("A mutation command identity is required.", nameof(commandId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        var mutation = TryLoad(commandId, out var loaded)
            ? loaded
            : throw new InvalidOperationException($"Salesforce mutation '{commandId}' has not been proposed.");
        EnsureSame(mutation, fingerprint);

        if (mutation.Status is MutationStatus.Completed or MutationStatus.OutcomeUncertain)
        {
            return Receipt(mutation);
        }

        if (mutation.Status is MutationStatus.Invoking)
        {
            mutation = mutation with { Status = MutationStatus.OutcomeUncertain };
            await SaveAsync(mutation);
            return Receipt(mutation);
        }

        if (mutation.Status is not MutationStatus.AwaitingApproval)
        {
            throw new InvalidOperationException(
                $"Salesforce mutation '{commandId}' cannot be approved from {mutation.Status}.");
        }

        mutation = mutation with { Status = MutationStatus.Approved };
        await SaveAsync(mutation);
        mutation = mutation with { Status = MutationStatus.Invoking };
        await SaveAsync(mutation);

        try
        {
            var content = await _transport.CallToolAsync(
                Endpoint,
                _authorization.CreateOptions(),
                "update_sobject_record",
                Arguments(mutation),
                CancellationToken.None);
            mutation = mutation with
            {
                Status = content.TryGetProperty("success", out var success)
                    && success.ValueKind is JsonValueKind.True
                        ? MutationStatus.Completed
                        : MutationStatus.OutcomeUncertain,
            };
        }
        catch (Exception)
        {
            mutation = mutation with { Status = MutationStatus.OutcomeUncertain };
        }

        await SaveAsync(mutation);
        return Receipt(mutation);
    }

    private static Dictionary<string, object?> Arguments(MutationData mutation)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sobject-name"] = "Account",
            ["id"] = mutation.AccountId,
            ["body"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Description"] = mutation.Description,
            },
        };

    private async Task SaveAsync(MutationData mutation, bool add = false)
    {
        var serialized = _states.SerializeToArray(mutation);

        if (add)
        {
            _mutations.Add(mutation.CommandId.Value, serialized);
        }
        else
        {
            _mutations[mutation.CommandId.Value] = serialized;
        }

        await WriteStateAsync();
    }

    private bool TryLoad(CommandId commandId, out MutationData mutation)
    {
        if (_mutations.TryGetValue(commandId.Value, out var serialized))
        {
            mutation = _states.Deserialize(serialized);
            return true;
        }

        mutation = null!;
        return false;
    }

    private static void EnsureSame(MutationData mutation, string fingerprint)
    {
        if (!string.Equals(mutation.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"CommandId '{mutation.CommandId}' is already bound to a different Salesforce mutation fingerprint.");
        }
    }

    private static SalesforceAccountDescriptionMutation Receipt(MutationData mutation)
        => new(
            mutation.CommandId,
            mutation.AccountId,
            mutation.Description,
            mutation.Fingerprint,
            mutation.Status switch
            {
                MutationStatus.Completed => SalesforceMutationState.Completed,
                MutationStatus.OutcomeUncertain => SalesforceMutationState.OutcomeUncertain,
                _ => SalesforceMutationState.AwaitingApproval,
            });

    private static string Fingerprint(string accountId, string description)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            tool = "update_sobject_record",
            sobject = "Account",
            id = accountId,
            body = new { Description = description },
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void Validate(CommandId commandId, string accountId, string description)
    {
        if (commandId == default)
        {
            throw new ArgumentException("A mutation command identity is required.", nameof(commandId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

    [GenerateSerializer]
    internal sealed record MutationData(
        [property: Id(0)] CommandId CommandId,
        [property: Id(1)] string AccountId,
        [property: Id(2)] string Description,
        [property: Id(3)] string Fingerprint,
        [property: Id(4)] MutationStatus Status);

    internal enum MutationStatus
    {
        Proposed,
        AwaitingApproval,
        Approved,
        Invoking,
        Completed,
        OutcomeUncertain,
    }
}
