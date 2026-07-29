namespace DigitalBrain.UI;

internal static class BrainEndpoints
{
    public static IEndpointRouteBuilder MapBrain(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            UiHttpContract.BrainTopologyPath,
            static async Task<IResult> (
                BrainTopologyReader topology,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(topology);
                var snapshot = await topology.ReadAsync(cancellationToken);
                return Results.Ok(snapshot);
            });

        return endpoints;
    }
}
