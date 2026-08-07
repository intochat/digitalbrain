namespace DigitalBrain.Product.Salesforce;

public sealed class SalesforceEffectNeuron : Neuron, INeuron<SalesforceInvocationRequested>
{
    public const string Kind = "salesforce-effect";

    private readonly ISalesforceGateway gateway;

    public SalesforceEffectNeuron(ISalesforceGateway gateway)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public async Task HandleAsync(SalesforceInvocationRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var mutation = synapse.Mutation;
        if (!string.Equals(Id.Name, mutation.MutationId, StringComparison.Ordinal))
        {
            return;
        }

        if (!Equals(Origin.Source, new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId)))
        {
            return;
        }

        SalesforceGatewayOutcome outcome;
        try
        {
            outcome = await gateway.ApplyOrReconcileAsync(mutation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            outcome = SalesforceGatewayOutcome.OutcomeUncertain;
        }

        var result = outcome == SalesforceGatewayOutcome.Confirmed
            ? (Synapse)new SalesforceChangeConfirmed(mutation)
            : new SalesforceChangeOutcomeUncertain(mutation);
        Emit(result, Dispatch.Direct(new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId)));
    }
}
