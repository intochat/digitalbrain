using Brain.Abstractions.Context;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Identity;
using Brain.Core.Endpoints;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
using Brain.Modules.Proof.Contracts;

namespace Brain.Modules.Proof;

internal interface IProofRouteService
{
    Task EnsureInitialAsync(ActivityContext context, CancellationToken cancellationToken);

    Task<CorrectionResult> ReplaceAsync(ActivityContext context, string requestedRoute, CancellationToken cancellationToken);
}

internal interface IProofActivityCompletion
{
    Task CompleteAsync(ActivityContext context, object result, Brain.Abstractions.Contracts.ContractId contract);
}

internal interface IProofDeliveryPump
{
    Task DispatchAsync(OutboxEntry entry, ProofProduced payload, CancellationToken cancellationToken);
}

internal sealed class ProofEntryNeuron(
    EndpointAddress endpoint,
    InMemoryOutboxStore<int> store,
    IGraphRouteResolver routes,
    ICapabilityBroker capabilities,
    IProofRouteService topology,
    ProofSourceNeuron source,
    TimeProvider clock)
    : BrainNeuron<int>(endpoint, store, routes, clock)
{
    internal Task AcceptAsync(ProofInput input, ActivityContext activity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return AcceptCoreAsync(input, activity, cancellationToken);
    }

    private async Task AcceptCoreAsync(ProofInput input, ActivityContext activity, CancellationToken cancellationToken)
    {
        ProofInput? directed = null;
        await ExecuteTurnAsync(
            activity,
            async turn =>
            {
                await topology.EnsureInitialAsync(activity, cancellationToken);
                var capabilityContext = new ActivityContext(
                    activity.Workspace,
                    activity.Principal,
                    activity.Activity,
                    activity.Correlation,
                    new Delegation([], [ProofContracts.Classifier]));
                var classified = await capabilities.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
                    ProofContracts.ClassifierCapability,
                    new CapabilityUseName("proof-classification"),
                    new ProofCapabilityInput(input.Value),
                    capabilityContext,
                    cancellationToken);
                SendAsync(turn, source.Address, ProofContracts.Input);
                turn.SetState(turn.State + 1);
                directed = new ProofInput(classified.Route);
                return 0;
            },
            cancellationToken);

        var message = store.DirectedMessages[^1];
        if (message.Target != source.Address || message.Contract != ProofContracts.Input || directed is null)
        {
            throw new InvalidOperationException("The proof entry can only hand off its committed direct send to the declared proof source.");
        }

        await source.EmitAsync(directed, activity, cancellationToken);
    }
}
