using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Mcp;

namespace DigitalBrain.OS.UiEdge;

internal static class McpOAuthCallbackEndpoints
{
    public static IEndpointRouteBuilder MapMcpOAuthCallback(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            UiEdgeContract.McpOAuthCallbackPath,
            static async Task<IResult> (
                string? state,
                string? code,
                string? error,
                string? iss,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(state))
                {
                    return Results.Text(
                        "DigitalBrain authorization callback rejected: missing state.",
                        "text/plain; charset=utf-8",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var authorization = brain.GetGrainProxy<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
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
