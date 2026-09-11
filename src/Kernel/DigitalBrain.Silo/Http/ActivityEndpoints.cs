using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/activities",
            static async Task<IResult> (int? limit, IGrainFactory grains, HttpContext http, CancellationToken cancellationToken) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var activities = grains.GetGrain<IActivities>(new NeuronId(UIVocabulary.ActivitiesType, UIVocabulary.ActivitiesInstance).ToGrainId());
                return Results.Ok(await activities.Read(new ReadActivities(limit ?? 100)).WaitAsync(cancellationToken));
            });

        endpoints.MapGet("/activities/events",
            static async Task (long? afterSequence, IGrainFactory grains, StreamWake wake, SessionStreamOptions options, HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var id = new NeuronId(UIVocabulary.ActivitiesType, UIVocabulary.ActivitiesInstance);
                var activities = grains.GetGrain<IActivities>(id.ToGrainId());
                await SessionStream.RunAsync(http, activities, id, JournalKind.Outgoing,
                    afterSequence.GetValueOrDefault(), ProjectActivity, "activity",
                    async token => await activities.Read(new ReadActivities()).WaitAsync(token), wake, options, cancellationToken);
            });

        endpoints.MapGet("/surfaces/{surfaceName}/activities",
            static async Task<IResult> (string surfaceName, IGrainFactory grains, HttpContext http,
                CancellationToken cancellationToken) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var surface = grains.GetGrain<ISurface>(new NeuronId(UIVocabulary.SurfaceType, surfaceName).ToGrainId());
                return Results.Ok(await ReadSurfaceAsync(surface, cancellationToken));
            }).AddEndpointFilter(new NeuronNameFilter("surfaceName"));

        endpoints.MapGet("/surfaces/{surfaceName}/activities/events",
            static async Task (string surfaceName, long? afterSequence, IGrainFactory grains, StreamWake wake, SessionStreamOptions options,
                HttpContext http, CancellationToken cancellationToken) =>
            {
                var id = new NeuronId(UIVocabulary.SurfaceType, surfaceName);
                var surface = grains.GetGrain<ISurface>(id.ToGrainId());
                await SessionStream.RunAsync(http, surface, id, JournalKind.Outgoing,
                    afterSequence.GetValueOrDefault(), ProjectActivity, "activity",
                    async token => await ReadSurfaceAsync(surface, token), wake, options, cancellationToken);
            }).AddEndpointFilter(new NeuronNameFilter("surfaceName"));

        endpoints.MapGet("/surfaces/{surfaceName}/activities/{activityId}/results",
            static async Task<IResult> (string surfaceName, string activityId, IGrainFactory grains, HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var surface = grains.GetGrain<ISurface>(new NeuronId(UIVocabulary.SurfaceType, surfaceName).ToGrainId());
                var state = await surface.Read().WaitAsync(cancellationToken);
                var activity = state.Activities?.FirstOrDefault(item => item.Id == activityId);
                if (activity is null)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "no-store";
                try
                {
                    var deliveries = new ConcurrentBag<SignalDelivery>();
                    await Parallel.ForEachAsync(activity.ParticipantNeuronIds.Distinct(StringComparer.Ordinal),
                        new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
                        async (participant, token) =>
                        {
                            if (!NeuronId.TryParse(participant, out var id))
                            {
                                return;
                            }

                            var neuron = grains.GetGrain<INeuron>(id.ToGrainId());
                            foreach (var kind in new[] { JournalKind.Incoming, JournalKind.Outgoing })
                            {
                                foreach (var delivery in await ReadRetainedAsync(neuron, kind, token))
                                {
                                    if (delivery.CorrelationId.ToString() == activity.CorrelationId)
                                    {
                                        deliveries.Add(delivery);
                                    }
                                }
                            }
                        });
                    return Results.Ok(deliveries.OrderBy(item => item.Timestamp)
                        .ThenBy(item => item.SignalId.ToString(), StringComparer.Ordinal)
                        .DistinctBy(item => item.SignalId).Select(item => ProjectResult(item, activity)).ToArray());
                }
                catch (ActivityResultsUnavailableException)
                {
                    return Results.Problem("The activity journals changed while reading their results. Retry the request.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }).AddEndpointFilter(new NeuronNameFilter("surfaceName"));

        return endpoints;
    }

    private static ActivityView? ProjectActivity(SignalDelivery delivery)
        => delivery.Signal.Type == UIVocabulary.ActivityChanged
            ? JsonSerializer.Deserialize(delivery.Signal.Body, UIJson.Default.ActivityChanged)?.Activity
            : null;

    private static async Task<ActivitiesSnapshot> ReadSurfaceAsync(ISurface surface, CancellationToken cancellationToken)
    {
        var state = await surface.Read().WaitAsync(cancellationToken);
        return new ActivitiesSnapshot(DateTimeOffset.UtcNow, state.Activities ?? []);
    }

    private static async Task<IReadOnlyList<SignalDelivery>> ReadRetainedAsync(
        INeuron neuron, JournalKind kind, CancellationToken cancellationToken)
    {
        var cursor = 0L;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var read = await neuron.ReadJournal(kind, cursor).WaitAsync(cancellationToken);
            if (!read.Gap)
            {
                return read.Delta;
            }

            cursor = Math.Max(0, read.EarliestRetained - 1);
        }

        throw new ActivityResultsUnavailableException();
    }

    private static ChatTurnEvent ProjectResult(SignalDelivery delivery, ActivityView activity)
    {
        var signalId = delivery.SignalId.ToString();
        if (delivery.Signal.Type == UIVocabulary.Responded
            && JsonSerializer.Deserialize(delivery.Signal.Body, UIJson.Default.Responded) is { } responded)
        {
            return new ChatTurnEvent(delivery.Sequence, false, responded.Text, responded.CommandId.ToString(),
                delivery.Signal.Type, delivery.Source.ToString(), activity.CorrelationId, delivery.Timestamp,
                responded.Turn.ToString(), Cards: responded.Cards, EventId: signalId);
        }

        var body = JsonNode.Parse(delivery.Signal.Body) as JsonObject;
        return new ChatTurnEvent(delivery.Sequence, delivery.Signal.Type == UIVocabulary.TurnRequested,
            body?["text"]?.ToString() ?? body?["detail"]?.ToString() ?? delivery.Signal.Body,
            body?["commandId"]?.ToString() ?? activity.CommandId ?? signalId,
            delivery.Signal.Type, delivery.Source.ToString(), activity.CorrelationId, delivery.Timestamp,
            body?["turn"]?.ToString(), EventId: signalId);
    }

    private sealed class ActivityResultsUnavailableException : Exception;
}
