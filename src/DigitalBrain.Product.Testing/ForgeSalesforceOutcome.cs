using DigitalBrain.Product.Salesforce;

namespace DigitalBrain.Product.Testing;

/// <summary>
/// Test-only ingress that simulates an arbitrary module reporting a Salesforce
/// result. The mutation behavior must accept results only from its effect
/// behavior.
/// </summary>
public sealed record ForgeSalesforceOutcome : Synapse
{
    public ForgeSalesforceOutcome(
        PreparedAccountDescriptionMutation mutation,
        SalesforceGatewayOutcome outcome)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        Outcome = outcome;
    }

    public PreparedAccountDescriptionMutation Mutation { get; }

    public SalesforceGatewayOutcome Outcome { get; }
}

public sealed class ForgedSalesforceOutcomeEmitter : Neuron, INeuron<ForgeSalesforceOutcome>
{
    public const string Kind = "forged-salesforce-outcome";

    public Task HandleAsync(ForgeSalesforceOutcome synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        Synapse result = synapse.Outcome == SalesforceGatewayOutcome.Confirmed
            ? new SalesforceChangeConfirmed(synapse.Mutation)
            : new SalesforceChangeOutcomeUncertain(synapse.Mutation);
        Emit(
            result,
            Dispatch.Direct(new NeuronId(SalesforceMutationNeuron.Kind, synapse.Mutation.MutationId)));
        return Task.CompletedTask;
    }
}
