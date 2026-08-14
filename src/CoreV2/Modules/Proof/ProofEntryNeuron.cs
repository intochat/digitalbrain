using Brain.Abstractions.Context;
using Brain.Core.Endpoints;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
using Brain.Modules.Proof.Contracts;

namespace Brain.Modules.Proof;

internal sealed class ProofEntryNeuron(
    EndpointAddress endpoint,
    InMemoryOutboxStore<int> store,
    IGraphRouteResolver routes,
    TimeProvider clock)
    : BrainNeuron<int>(endpoint, store, routes, clock)
{
    internal Task AcceptAsync(
        ProofInput input,
        EndpointAddress source,
        ActivityContext activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ExecuteTurnAsync(
            activity,
            turn =>
            {
                SendAsync(turn, source, ProofContracts.Input);
                turn.SetState(turn.State + 1);
                return Task.FromResult(0);
            },
            cancellationToken);
    }
}
