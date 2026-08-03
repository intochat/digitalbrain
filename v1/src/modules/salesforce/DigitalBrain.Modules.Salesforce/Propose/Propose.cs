using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

internal sealed partial class Salesforce
{
    private async Task<SalesforceAccountDescriptionMutation> ProposeAccountDescriptionAsync(
        CommandId commandId,
        NeuronId requester,
        string accountId,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProposal(commandId, accountId, description);

        var fingerprint = Fingerprint(accountId, description);

        if (TryLoad(commandId, out var existing))
        {
            EnsureSame(existing, fingerprint);
            return Receipt(existing);
        }

        var proposed = new MutationData(
            commandId,
            requester,
            accountId,
            description,
            fingerprint,
            UpdateSchemaFingerprint: null,
            QuerySchemaFingerprint: null,
            Approval: null,
            ApprovalEvidence: null,
            MutationStatus.AwaitingApproval);
        await SaveAsync(proposed, add: true);

        return Receipt(proposed);
    }

    private static void ValidateProposal(CommandId commandId, string accountId, string description)
    {
        if (commandId == default)
        {
            throw new ArgumentException("A mutation command identity is required.", nameof(commandId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ValidateAccountId(accountId);
    }
}
