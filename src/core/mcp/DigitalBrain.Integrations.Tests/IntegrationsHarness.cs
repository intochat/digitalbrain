using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Integrations.Tests;

[ClientEntryPoint]
[Alias("integrations.driver")]
[Description("Integration harness driver neuron")]
public partial interface IIntegrationDriver : INeuron
{
    [Alias(nameof(ProposeSalesforceAccountDescription))]
    Task<SalesforceAccountDescriptionMutation> ProposeSalesforceAccountDescription(
        CommandId commandId,
        string accountId,
        string description,
        CancellationToken cancellationToken);

    [Alias(nameof(ApproveSalesforceWithStoredEvidence))]
    Task<SalesforceAccountDescriptionMutation> ApproveSalesforceWithStoredEvidence(
        SalesforceMutationApproval approval,
        CancellationToken cancellationToken);

    [Alias(nameof(ApproveSalesforceWithMismatchedEvidence))]
    Task ApproveSalesforceWithMismatchedEvidence(
        SalesforceMutationApproval approval,
        SalesforceMutationApproval recordedEvidence,
        CancellationToken cancellationToken);
}

internal sealed class IntegrationDriver :
    Neuron,
    IIntegrationDriver,
    IHandle<SalesforceMutationApproval>
{
    public Task HandleAsync(SalesforceMutationApproval synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<SalesforceAccountDescriptionMutation> ProposeSalesforceAccountDescription(
        CommandId commandId,
        string accountId,
        string description,
        CancellationToken cancellationToken)
        => Salesforce().ProposeAccountDescription(commandId, Id, accountId, description, cancellationToken);

    public async Task<SalesforceAccountDescriptionMutation> ApproveSalesforceWithStoredEvidence(
        SalesforceMutationApproval approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var evidence = await ApprovalEvidenceAsync(approval);
        return await Salesforce().ApproveAccountDescription(approval, evidence, cancellationToken);
    }

    public async Task ApproveSalesforceWithMismatchedEvidence(
        SalesforceMutationApproval approval,
        SalesforceMutationApproval recordedEvidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(recordedEvidence);
        var evidence = await ApprovalEvidenceAsync(recordedEvidence);
        _ = await Salesforce().ApproveAccountDescription(approval, evidence, cancellationToken);
    }

    private ISalesforce Salesforce()
        => GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, IntegrationsFixture.SalesforceServerKey).ToGrainId());

    private async Task<SynapseDelivery> ApprovalEvidenceAsync(SalesforceMutationApproval approval)
    {
        var incoming = await ReadJournal(JournalKind.Incoming, afterSequence: 0);
        return incoming.Delta.FirstOrDefault(delivery =>
                delivery.Caller == approval.Approver
                && delivery.Synapse is SalesforceMutationApproval recorded
                && recorded == approval)
            ?? throw new InvalidOperationException(
                $"Approval '{approval.ApprovalId}' has no durable human delivery evidence.");
    }
}

public sealed partial class IntegrationsHarnessModule : IModule;
