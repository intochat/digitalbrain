using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Mcp;

namespace DigitalBrain.UI;

internal static class McpOAuthCallbackEndpoints
{
    public static IEndpointRouteBuilder MapMcpOAuthCallback(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            UiHttpContract.McpOAuthCallbackPath,
            static async Task<IResult> (
                string? state,
                string? code,
                string? error,
                string? iss,
                IDigitalBrain brain,
                IGrainFactory grains,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(grains);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(state))
                {
                    return Results.Text(
                        "DigitalBrain authorization callback rejected: missing state.",
                        "text/plain; charset=utf-8",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var authorization = grains.GetGrain<IMcpAuthorization>(
                    NeuronId.For<IMcpAuthorization>(brain.Owner, McpAuthorizationNeuron.InstanceName).ToGrainId());
                var delivery = await authorization.DeliverCallback(
                    new DeliverMcpAuthorizationCallback(state, code, error, iss),
                    cancellationToken);

                if (!delivery.Accepted)
                {
                    return Results.Text(
                        "DigitalBrain authorization callback rejected: unknown state.",
                        "text/plain; charset=utf-8",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                if (delivery.Denied)
                {
                    return Results.Text(
                        "DigitalBrain authorization was denied. You can close this window.",
                        "text/plain; charset=utf-8");
                }

                return Results.Text(
                    "DigitalBrain authorization completed. You can close this window.",
                    "text/plain; charset=utf-8");
            });

        return endpoints;
    }
}
