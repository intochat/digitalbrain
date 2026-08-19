using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Client;

namespace DigitalBrain.Kernel;

internal static class BrainStreamsHttpMaps
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public static IEndpointRouteBuilder MapBrainStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.GraphEventsPath,
            static async Task (
                HttpContext http,
                long? afterSequence,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (!HttpActor.TryGet(http, out _))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    WatchConnectionChangesAsync(brain, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    // The brain keeps its connection table as plain entity state — no journal — so changes
    // are observed by polling and diffing. The first read replays the current table as
    // "connected" events (a late subscriber's catch-up); sequence numbers belong to this
    // stream alone and only order its own events.
    private static async IAsyncEnumerable<SseItem<GraphEvent>> WatchConnectionChangesAsync(
        IDigitalBrain brain,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var registry = brain.GetEntity<IBrain>(DigitalBrainNames.DefaultBrain);
        var known = new Dictionary<string, Connection>(StringComparer.Ordinal);
        var sequence = afterSequence;

        while (!cancellationToken.IsCancellationRequested)
        {
            var state = await registry.Read().ConfigureAwait(false);
            var current = (state?.Connections ?? [])
                .ToDictionary(IdentityOf, static c => c, StringComparer.Ordinal);

            foreach (var (identity, gone) in known.Where(entry => !current.ContainsKey(entry.Key)).ToArray())
            {
                known.Remove(identity);
                yield return ChangeEvent("disconnected", ++sequence, identity, gone);
            }

            foreach (var (identity, live) in current)
            {
                if (known.TryAdd(identity, live))
                {
                    yield return ChangeEvent("connected", ++sequence, identity, live);
                }
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string IdentityOf(Connection connection)
        => ConnectionIdentity.Of(
            connection.From.ToString(),
            connection.Role,
            connection.To.ToString());

    private static SseItem<GraphEvent> ChangeEvent(
        string kind,
        long sequence,
        string identity,
        Connection connection)
        => new(
            new GraphEvent(
                sequence,
                kind,
                identity,
                connection.From.ToString(),
                connection.Role,
                connection.To.ToString(),
                DateTimeOffset.UtcNow),
            HttpSurfacePaths.GraphChangeEvent)
        {
            EventId = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}
