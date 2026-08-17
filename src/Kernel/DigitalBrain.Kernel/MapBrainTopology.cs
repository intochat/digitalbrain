using DigitalBrain.Client;
using DigitalBrain.Introspection;

namespace DigitalBrain.Kernel;

internal static class BrainTopologyHttpMaps
{
    internal static readonly TimeSpan TopologyReplyBound = TimeSpan.FromSeconds(90);

    public static IEndpointRouteBuilder MapBrainTopology(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.BrainTopologyPath,
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
                        .FireAsync(new ReadTopologyRequest(), deadline.Token)
                        .ConfigureAwait(false);
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
                    read.ObservedAt,
                    [
                        .. read.Connections.Select(static connection => new BrainConnection(
                            connection.ConnectionId,
                            connection.Source,
                            connection.SynapseAlias,
                            connection.Target,
                            connection.Transform,
                            connection.ExpiresAt)),
                    ],
                    [
                        .. read.BroadcastRoutes.Select(static route => new BrainBroadcastRoute(
                            route.SynapseAlias,
                            route.HandlerGrainType)),
                    ]));
            });

        return endpoints;
    }
}
