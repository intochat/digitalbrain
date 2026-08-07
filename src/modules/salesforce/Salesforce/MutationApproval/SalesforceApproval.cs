using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

internal sealed partial class Salesforce
{
    private async Task<SalesforceAccountDescriptionMutation> ApproveAccountDescriptionAsync(
        SalesforceMutationApproval approval,
        SynapseDelivery approvalEvidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(approvalEvidence);
        ValidateApproval(approval, approvalEvidence);

        var mutation = TryLoad(approval.CommandId, out var loaded)
            ? loaded
            : throw new InvalidOperationException(
                $"Salesforce mutation '{approval.CommandId}' has not been proposed.");
        EnsureSame(mutation, approval.Fingerprint);

        if (mutation.Status is MutationStatus.Completed or MutationStatus.OutcomeUncertain)
        {
            ValidateApprovalEvidence(mutation, approval, approvalEvidence);
            EnsureSameApproval(mutation, approval);
            return Receipt(mutation);
        }

        if (mutation.Status is MutationStatus.Invoking)
        {
            ValidateApprovalEvidence(mutation, approval, approvalEvidence);
            EnsureSameApproval(mutation, approval);
            mutation = await ReconcileBoundedAsync(mutation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SaveAsync(mutation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return Receipt(mutation);
        }

        if (mutation.Status is not MutationStatus.AwaitingApproval)
        {
            throw new InvalidOperationException(
                $"Salesforce mutation '{approval.CommandId}' cannot be approved from {mutation.Status}.");
        }

        ValidateApprovalEvidence(mutation, approval, approvalEvidence);
        cancellationToken.ThrowIfCancellationRequested();

        var admitted = await _runtime.RunAsync(
            Server,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            approval.CommandId,
            Id.Owner,
            GrainFactory,
            async (client, callbackCancellation) =>
            {
                var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation).ConfigureAwait(true);
                return (
                    Update: SelectUpdateTool(tools).Fingerprint,
                    Query: SelectQueryTool(tools).Fingerprint);
            },
            cancellationToken).ConfigureAwait(true);
        mutation = mutation with
        {
            Approval = approval,
            ApprovalEvidence = approvalEvidence.SynapseId,
            UpdateSchemaFingerprint = admitted.Update,
            QuerySchemaFingerprint = admitted.Query,
            Status = MutationStatus.Invoking,
        };
        await SaveAsync(mutation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        try
        {
            mutation = await InvokeUpdateAsync(mutation, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception)
        {
            mutation = await ReconcileBoundedAsync(mutation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (mutation.Status is MutationStatus.Invoking)
        {
            mutation = await ReconcileBoundedAsync(mutation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await SaveAsync(mutation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return Receipt(mutation);
    }

    private static void EnsureSameApproval(MutationData mutation, SalesforceMutationApproval approval)
    {
        if (mutation.Approval != approval)
        {
            throw new NeuronAuthorizationException(
                $"Salesforce mutation '{mutation.CommandId}' is bound to different approval evidence.");
        }
    }

    private static void ValidateApprovalEvidence(
        MutationData mutation,
        SalesforceMutationApproval approval,
        SynapseDelivery evidence)
    {
        if (evidence.Caller != approval.Approver)
        {
            throw new NeuronAuthorizationException(
                $"Salesforce mutation '{mutation.CommandId}' has no exact durable human approval evidence.");
        }

        var matches = evidence.Synapse switch
        {
            SalesforceMutationApproval recorded => recorded == approval,
            ApproveSalesforceMutation request => request.Approval == approval,
            _ => false,
        };

        if (!matches)
        {
            throw new NeuronAuthorizationException(
                $"Salesforce mutation '{mutation.CommandId}' has no exact durable human approval evidence.");
        }

        if (mutation.Approval is not null
            && mutation.ApprovalEvidence is not null
            && mutation.ApprovalEvidence != evidence.SynapseId
            && evidence.Synapse is not ApproveSalesforceMutation)
        {
            throw new NeuronAuthorizationException(
                $"Salesforce mutation '{mutation.CommandId}' has no exact durable human approval evidence.");
        }
    }

    private void ValidateApproval(SalesforceMutationApproval approval, SynapseDelivery approvalEvidence)
    {
        if (approval.CommandId == default)
        {
            throw new ArgumentException("A mutation command identity is required.", nameof(approval));
        }

        if (approval.ApprovalId == Guid.Empty || approvalEvidence.SynapseId == default)
        {
            throw new ArgumentException("Durable approval identity and evidence are required.", nameof(approval));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(approval.Fingerprint);

        if (approval.Approver.Owner != Id.Owner
            || approval.Approver.Type != ISessionNeuron.GrainTypeName
            || approval.ApprovedAt == default)
        {
            throw new NeuronAuthorizationException(
                "Salesforce mutation approval must be issued by this owner's human session.");
        }
    }
}
