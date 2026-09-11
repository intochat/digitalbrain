using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/chats/{chatName}/send",
            static async Task<IResult> (string chatName, SendChatRequest request, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return Results.BadRequest();
                }

                try
                {
                    var chat = grains.GetGrain<IChat>(new NeuronId(UIVocabulary.ChatType, chatName).ToGrainId());
                    var command = request.CommandId is { } id ? new CommandId(id) : CommandId.New();
                    var accepted = await chat.Send(new SendMessage(command, request.Text, request.Context), cancellationToken);
                    return Results.Accepted(value: accepted);
                }
                catch (Exception error) when (EdgeResults.IsRejection(error))
                {
                    return EdgeResults.Rejected(error);
                }
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        endpoints.MapPost("/chats/{chatName}/turns/{turn}/cancel",
            static async Task<IResult> (string chatName, string turn, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                if (!Guid.TryParse(turn, out var id) || id == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                try
                {
                    var chat = grains.GetGrain<IChat>(new NeuronId(UIVocabulary.ChatType, chatName).ToGrainId());
                    var accepted = await chat.Cancel(new CancelTurn(CommandId.New(), new SignalId(id)), cancellationToken);
                    return Results.Accepted(value: accepted);
                }
                catch (Exception error) when (EdgeResults.IsRejection(error))
                {
                    return EdgeResults.Rejected(error);
                }
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        endpoints.MapGet("/chats/{chatName}/transcript",
            static async Task<IResult> (string chatName, int? maxTurns, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                var chat = grains.GetGrain<IChat>(new NeuronId(UIVocabulary.ChatType, chatName).ToGrainId());
                return Results.Ok(await chat.ReadTranscript(new ReadTranscript(maxTurns)).WaitAsync(cancellationToken));
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        endpoints.MapGet("/chats/{chatName}/turns",
            static async Task<IResult> (string chatName, int? maxTurns, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                var chat = grains.GetGrain<IChat>(new NeuronId(UIVocabulary.ChatType, chatName).ToGrainId());
                return Results.Ok(await chat.ReadTurns(new ReadTurns(maxTurns)).WaitAsync(cancellationToken));
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        endpoints.MapGet("/chats/{chatName}/turns/{turn}",
            static async Task<IResult> (string chatName, string turn, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                if (!Guid.TryParse(turn, out var id) || id == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                var chat = grains.GetGrain<IChat>(new NeuronId(UIVocabulary.ChatType, chatName).ToGrainId());
                var snapshot = await chat.ReadTurn(new ReadTurn(new SignalId(id))).WaitAsync(cancellationToken);
                return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        endpoints.MapGet("/chats/{chatName}/events",
            static async Task (string chatName, long? afterSequence, IGrainFactory grains, StreamWake wake, SessionStreamOptions options,
                HttpContext http, IConfiguration configuration, CancellationToken cancellationToken) =>
            {
                var chatId = new NeuronId(UIVocabulary.ChatType, chatName);
                var chat = grains.GetGrain<IChat>(chatId.ToGrainId());
                var sessionId = SessionNeuron.For(configuration);
                await SessionStream.RunAsync(http, grains.GetGrain<INeuron>(sessionId.ToGrainId()), sessionId,
                    JournalKind.Incoming, afterSequence.GetValueOrDefault(), delivery => ProjectTurn(delivery, chatId),
                    "chat-turn", async token => await chat.ReadTurns(new ReadTurns()).WaitAsync(token),
                    wake, options, cancellationToken, resetOnConnect: true);
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        return endpoints;
    }

    private static ChatTurnEvent? ProjectTurn(SignalDelivery delivery, NeuronId chat)
    {
        if (delivery.Signal.Type == UIVocabulary.Responded)
        {
            var responded = JsonSerializer.Deserialize(delivery.Signal.Body, UIJson.Default.Responded);
            return responded is null || responded.Chat != chat ? null : new ChatTurnEvent(
                delivery.Sequence, false, responded.Text, responded.CommandId.ToString(), delivery.Signal.Type,
                chat.ToString(), delivery.CorrelationId.ToString(), delivery.Timestamp, responded.Turn.ToString(),
                nameof(ChatTurnStatus.Completed), responded.Cards, delivery.SignalId.ToString());
        }

        if (delivery.Signal.Type is not (UIVocabulary.TurnAccepted or UIVocabulary.TurnFailed) || delivery.Source != chat)
        {
            return null;
        }

        // TurnAccepted and TurnFailed name their chat only in the envelope's source.
        var body = JsonNode.Parse(delivery.Signal.Body);
        if (delivery.Signal.Type == UIVocabulary.TurnAccepted)
        {
            var accepted = JsonSerializer.Deserialize(delivery.Signal.Body, UIJson.Default.TurnAccepted);
            return accepted is null ? null : new ChatTurnEvent(delivery.Sequence, true, accepted.Text,
                accepted.CommandId.ToString(), delivery.Signal.Type, chat.ToString(),
                delivery.CorrelationId.ToString(), delivery.Timestamp, accepted.Turn.ToString(),
                null, EventId: delivery.SignalId.ToString());
        }

        return new ChatTurnEvent(delivery.Sequence, false, body?["detail"]?.GetValue<string>() ?? string.Empty,
            body?["commandId"]?.GetValue<string>() ?? string.Empty, delivery.Signal.Type, chat.ToString(),
            delivery.CorrelationId.ToString(), delivery.Timestamp, body?["turn"]?.GetValue<string>(),
            body?["status"]?.GetValue<string>() ?? nameof(ChatTurnStatus.Failed), EventId: delivery.SignalId.ToString());
    }
}

internal sealed record SendChatRequest(Guid? CommandId, string Text, IReadOnlyList<ContextRef>? Context = null);
