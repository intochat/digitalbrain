using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class GraphEndpoints
{
    public static IEndpointRouteBuilder MapGraphEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapBrainObservationEndpoints();
        return endpoints.MapGraphMutationEndpoints();
    }

    public static IEndpointRouteBuilder MapBrainObservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/chats/{chatName}/brain",
            static async Task<IResult> (string chatName, IGrainFactory grains, HttpContext http, IConfiguration configuration,
                CancellationToken cancellationToken) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await BrainGraphProjection.ReadAsync(grains,
                    new NeuronId(UIVocabulary.ChatType, chatName), SessionNeuron.For(configuration), cancellationToken));
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        endpoints.MapGet("/chats/{chatName}/brain/events",
            static async Task (string chatName, long? afterSequence, IGrainFactory grains, StreamWake wake, SessionStreamOptions options,
                HttpContext http, IConfiguration configuration, CancellationToken cancellationToken) =>
            {
                var chat = new NeuronId(UIVocabulary.ChatType, chatName);
                var session = SessionNeuron.For(configuration);
                await SessionStream.RunSnapshotAsync(http, grains.GetGrain<INeuron>(chat.ToGrainId()), chat,
                    JournalKind.Outgoing, afterSequence.GetValueOrDefault(),
                    token => BrainGraphProjection.ReadAsync(grains, chat, session, token), "brain-snapshot", wake, options, cancellationToken);
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        return endpoints;
    }

    public static IEndpointRouteBuilder MapGraphMutationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/chats/{chatName}/brain/subscriptions",
            static async Task<IResult> (BrainGraphSubscriptionRequest request, IGrainFactory grains,
                CancellationToken cancellationToken) =>
            {
                if (!NeuronId.TryParse(request.SourceId, out var source)
                    || !NeuronId.TryParse(request.TargetId, out var target)
                    || request.SignalType is not { Length: > 0 and <= 64 }
                    || !request.SignalType.All(char.IsAsciiLetter))
                {
                    return Results.BadRequest();
                }

                var neuron = grains.GetGrain<INeuron>(source.ToGrainId());
                await (request.Subscribed ? neuron.Connect(target, request.SignalType) : neuron.Disconnect(target, request.SignalType))
                    .WaitAsync(cancellationToken);
                return Results.Ok(new BrainGraphSubscriptionResult(request.SourceId, request.TargetId,
                    request.SignalType, request.Subscribed));
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        endpoints.MapGet("/chats/{chatName}/activities",
            static async Task<IResult> (string chatName, IGrainFactory grains, HttpContext http, IConfiguration configuration,
                CancellationToken cancellationToken) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var snapshot = await BrainGraphProjection.ReadAsync(grains,
                    new NeuronId(UIVocabulary.ChatType, chatName), SessionNeuron.For(configuration), cancellationToken);
                return Results.Ok(new { snapshot.ObservedAt, snapshot.Truncated, snapshot.Correlations });
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        return endpoints;
    }
}
