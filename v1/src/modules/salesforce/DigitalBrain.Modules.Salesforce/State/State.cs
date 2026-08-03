using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

internal sealed partial class Salesforce
{
    private async Task SaveAsync(MutationData mutation, bool add = false)
    {
        var key = mutation.CommandId.Value;
        var serialized = _states.SerializeToArray(mutation);
        var existed = _mutations.TryGetValue(key, out var previous);

        try
        {
            if (add)
            {
                _mutations.Add(key, serialized);
            }
            else
            {
                _mutations[key] = serialized;
            }

            await WriteStateAsync();
        }
        catch
        {
            if (existed)
            {
                _mutations[key] = previous!;
            }
            else
            {
                _mutations.Remove(key);
            }

            throw;
        }
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
            tool = UpdateAccountName,
            sobject = "Account",
            id = accountId,
            body = new { Description = description },
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void ValidateAccountId(string accountId)
    {
        if (accountId.Length is not (15 or 18)
            || accountId.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "A Salesforce Account ID must be a 15- or 18-character alphanumeric value.",
                nameof(accountId));
        }
    }

    [GenerateSerializer]
    internal sealed record MutationData(
        [property: Id(0)] CommandId CommandId,
        [property: Id(1)] NeuronId Requester,
        [property: Id(2)] string AccountId,
        [property: Id(3)] string Description,
        [property: Id(4)] string Fingerprint,
        [property: Id(5)] string? UpdateSchemaFingerprint,
        [property: Id(6)] string? QuerySchemaFingerprint,
        [property: Id(7)] SalesforceMutationApproval? Approval,
        [property: Id(8)] SynapseId? ApprovalEvidence,
        [property: Id(9)] MutationStatus Status);

    internal enum MutationStatus
    {
        AwaitingApproval,
        Invoking,
        Completed,
        OutcomeUncertain,
    }
}
