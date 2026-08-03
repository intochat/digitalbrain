using DigitalBrain.Client;
using DigitalBrain.Introspection;

namespace DigitalBrain.OS.UiEdge;

internal static class BrainEndpoints
{
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

                var read = await brain
                    .Get<IIntrospection>()
                    .SendAsync(new ReadTopologyRequest(), cancellationToken);
                if (read.Error is { } refused)
                {
                    return Results.Problem(refused);
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
