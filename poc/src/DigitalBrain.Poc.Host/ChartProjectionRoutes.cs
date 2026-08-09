using System.Net;
using System.Net.Http.Headers;
using DigitalBrain.Poc.Charting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Poc.Host;

public static class ChartProjectionRoutes
{
    public static IEndpointConventionBuilder MapChartProjectionRoutes(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGet(
            "/poc/charts/{chartId}",
            async (
                string chartId,
                HttpRequest request,
                TestOwnerAuthority owners,
                ChartProjectionEndpoint charts,
                CancellationToken cancellationToken) =>
            {
                var token = ReadBearerToken(request.Headers.Authorization);
                var response = await GetAsync(token, chartId, owners, charts, cancellationToken);
                return response.StatusCode switch
                {
                    HttpStatusCode.OK => Results.Ok(response.Snapshot),
                    HttpStatusCode.NotFound => Results.NotFound(),
                    _ => Results.Unauthorized(),
                };
            });
    }

    public static async Task<Response> GetAsync(
        string? bearerToken,
        string chartId,
        TestOwnerAuthority owners,
        ChartProjectionEndpoint charts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(charts);
        if (string.IsNullOrWhiteSpace(bearerToken) ||
            !owners.TryResolveToken(bearerToken, out var principal))
        {
            return new Response(HttpStatusCode.Unauthorized, null);
        }

        var snapshot = await charts.ReadAsync(principal.OwnerId, chartId, cancellationToken);
        return snapshot is null
            ? new Response(HttpStatusCode.NotFound, null)
            : new Response(HttpStatusCode.OK, snapshot);
    }

    public sealed record Response(HttpStatusCode StatusCode, ChartNeuron.Snapshot? Snapshot);

    private static string? ReadBearerToken(string? authorization)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return null;
        }

        return header.Parameter;
    }
}
