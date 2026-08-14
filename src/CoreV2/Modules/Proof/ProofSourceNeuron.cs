using Brain.Abstractions.Context;
using Brain.Core.Endpoints;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
using Brain.Modules.Proof.Contracts;

namespace Brain.Modules.Proof;

internal sealed class ProofSourceNeuron(
    EndpointAddress endpoint,
    InMemoryOutboxStore<int> store,
    IGraphRouteResolver routes,
    IProofDeliveryPump deliveries,
    TimeProvider clock)
    : BrainNeuron<int>(endpoint, store, routes, clock)
{
    internal EndpointAddress Address { get; } = endpoint;

    internal async Task EmitAsync(ProofInput input, ActivityContext activity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await ExecuteTurnAsync(
            activity,
            async turn =>
            {
                turn.SetState(turn.State + 1);
                await EmitAsync(turn, new ProofProduced(input.Value), ProofContracts.Produced, cancellationToken);
                return 0;
            },
            cancellationToken);
        await deliveries.DispatchAsync(store.Emissions[^1], new ProofProduced(input.Value), cancellationToken);
    }
}
