using DigitalBrain.Product.Salesforce;

namespace DigitalBrain.Product.Testing;

/// <summary>
/// Test-only ingress that simulates an arbitrary module asking the Salesforce
/// effect behavior to invoke a mutation.
/// </summary>
public sealed record ForgeSalesforceInvocation : Synapse
{
    public ForgeSalesforceInvocation(PreparedAccountDescriptionMutation mutation)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    public PreparedAccountDescriptionMutation Mutation { get; }
}

public sealed class ForgedSalesforceInvocationEmitter : Neuron, INeuron<ForgeSalesforceInvocation>
{
    public const string Kind = "forged-salesforce-invocation";

    public Task HandleAsync(ForgeSalesforceInvocation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        Emit(
            new SalesforceInvocationRequested(synapse.Mutation),
            Dispatch.Direct(new NeuronId(SalesforceEffectNeuron.Kind, synapse.Mutation.MutationId)));
        return Task.CompletedTask;
    }
}
