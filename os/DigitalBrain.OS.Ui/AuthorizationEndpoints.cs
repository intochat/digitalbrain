namespace DigitalBrain.Flutter.Http;

internal static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizations(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            FlutterHttpContract.AuthorizationEventsPath,
            static async Task (
                HttpContext http,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    AuthorizationEventFeed.WatchAuthorizationsAsync(sessionJournal, cursor, cancellationToken),
                    cancellationToken);
            });

        return endpoints;
    }
}
