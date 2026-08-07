using DigitalBrain.Client;
using DigitalBrain.Introspection;

namespace DigitalBrain.UiEdge;

internal static class BrainEndpoints
{
    // A directed request is fired, handled and replied through the outbox, so the answer can outlive
    // a single delivery attempt. The session journal watch it waits on carries no deadline of its
    // own, so without this an introspection neuron that never answers holds the HTTP request open
    // until the client gives up, well past the grain-call response timeout it exists to tighten.
    internal static readonly TimeSpan TopologyReplyBound = TimeSpan.FromSeconds(90);

    public static IEndpointRouteBuilder MapBrain(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            UiEdgeContract.BrainTopologyPath,
            static async Task<IResult> (
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TopologyReplyBound);

                TopologyRead read;
                try
                {
                    read = await brain
                        .Get<IIntrospection>()
                        .SendAsync(new ReadTopologyRequest(), deadline.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Results.Problem(
                        "The introspection neuron did not report topology within "
                        + $"{TopologyReplyBound.TotalSeconds} seconds.",
                        statusCode: StatusCodes.Status504GatewayTimeout);
                }

                return Results.Ok(new BrainTopologySnapshot(
                    [.. read.Modules.Select(static module => new BrainModule(module))],
                    [
                        .. read.Neurons.Select(static neuron => new BrainNeuron(
                            neuron.Id,
                            neuron.GrainType,
                            neuron.Identity,
                            neuron.Placement)),
                    ],
                    read.ObservedAt));
            });

        return endpoints;
    }
}
