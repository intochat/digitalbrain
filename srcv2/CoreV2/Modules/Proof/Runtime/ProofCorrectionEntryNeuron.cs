using Brain.Abstractions.Context;
using Brain.Core.Endpoints;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
using Brain.Modules.Proof.Contracts;

namespace Brain.Modules.Proof;

internal sealed class ProofCorrectionEntryNeuron(
    EndpointAddress endpoint,
    InMemoryOutboxStore<int> store,
    IGraphRouteResolver routes,
    IProofRouteService topology,
    IProofActivityCompletion activities,
    TimeProvider clock)
    : BrainNeuron<int>(endpoint, store, routes, clock)
{
    internal Task AcceptAsync(CorrectionInput input, ActivityContext activity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ExecuteTurnAsync(
            activity,
            async turn =>
            {
                if (input.RequestedRoute is not ("summary" or "assessment"))
                {
                    throw new ArgumentException("The requested proof route is not supported.", nameof(input));
                }

                var result = await topology.ReplaceAsync(activity, input.RequestedRoute, cancellationToken);
                turn.SetState(turn.State + 1);
                await activities.CompleteAsync(activity, result, ProofContracts.CorrectionResult);
                return 0;
            },
            cancellationToken);
    }
}
