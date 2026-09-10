using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.ChatType)]
internal sealed class ChatNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChatState> state)
    : Neuron<ChatState>(runtime, state), IChat
{
    private NeuronId Responder => State?.Agent ?? new NeuronId(AIVocabulary.AgentType, Id.Name);

    public Task<Accepted<SignalId>> Send(SendMessage message, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("send"), message, UIJson.Default.SendMessage, UIJson.Default.AcceptedSignalId, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Text);
            RequirePayloadSize(arguments.Text);
            foreach (var reference in arguments.Context ?? [])
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(reference.Path);
                ArgumentException.ThrowIfNullOrWhiteSpace(reference.SchemaHash);
                if (reference.PayloadJson is { } payload)
                {
                    RequirePayloadSize(payload);
                }
            }

            var work = Schedule(Signal.Create(UIVocabulary.TurnRequested, ChatBodies.Requested(arguments)));
            return new Accepted<SignalId>(work, work);
        });

    public Task<Accepted<ChatTurnStatus>> Cancel(CancelTurn command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("cancel"), command, UIJson.Default.CancelTurn, UIJson.Default.AcceptedChatTurnStatus, arguments =>
        {
            var turn = Find(arguments.Turn)?.Snapshot
                ?? throw new ArgumentException("The turn does not exist. Read the chat turns and cancel a running turn by its turn id.", nameof(command));
            if (turn.Status != ChatTurnStatus.Running)
            {
                throw new ArgumentException($"The turn is {turn.Status}. Only cancel a Running turn; send a new message to start another turn.", nameof(command));
            }

            var work = Schedule(Signal.Create(UIVocabulary.TurnCancelling,
                new JsonObject { ["turn"] = arguments.Turn.ToString() }.ToJsonString()));
            return new Accepted<ChatTurnStatus>(ChatTurnStatus.Cancelled, work);
        });

    [ReadOnly]
    public Task<ChatTranscript> ReadTranscript(ReadTranscript query)
        => Task.FromResult(new ChatTranscript([.. (State?.Transcript ?? []).TakeLast(Math.Clamp(query.MaxTurns ?? ChatState.MaxTranscript, 1, ChatState.MaxTranscript))]));

    [ReadOnly]
    public Task<ChatTurns> ReadTurns(ReadTurns query)
        => Task.FromResult(new ChatTurns([.. (State?.Turns ?? []).TakeLast(Math.Clamp(query.MaxTurns ?? ChatState.MaxTurns, 1, ChatState.MaxTurns)).Select(turn => turn.Snapshot)]));

    [ReadOnly]
    public Task<ChatTurnSnapshot?> ReadTurn(ReadTurn query) => Task.FromResult(Find(query.Turn)?.Snapshot);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(delivery.Signal.Body);
        switch (delivery.Signal.Type)
        {
            // Chat Instruct reuses AIVocabulary.Instruct with { "agent": "agent:name" }.
            case AIVocabulary.Instruct:
                if (NeuronId.TryParse(ChatBodies.String(body, "agent"), out var agent))
                {
                    await SaveAsync(Current() with { Agent = agent }, cancellationToken).ConfigureAwait(true);
                }

                break;
            case UIVocabulary.TurnRequested:
                await RequestAsync(delivery, body, cancellationToken).ConfigureAwait(true);
                break;
            case AIVocabulary.Reply:
            case AIVocabulary.Said:
                await AnswerAsync(delivery, body, cancellationToken).ConfigureAwait(true);
                break;
            case UIVocabulary.ChartRendered:
            case UIVocabulary.GraphRendered:
            case UIVocabulary.ImageDescribed:
            case UIVocabulary.SheetChanged:
                await OfferAsync(delivery, body, cancellationToken).ConfigureAwait(true);
                break;
            case UIVocabulary.TurnCancelling:
                if (Guid.TryParse(ChatBodies.String(body, "turn"), out var turn) && turn != Guid.Empty)
                {
                    await CancelAsync(new SignalId(turn), cancellationToken).ConfigureAwait(true);
                }

                break;
        }
    }

    private async Task RequestAsync(SignalDelivery delivery, JsonNode? body, CancellationToken cancellationToken)
    {
        var turn = delivery.SignalId;
        if (!CommandId.TryParse(ChatBodies.String(body, "commandId"), out var commandId))
        {
            return;
        }

        try
        {
            var record = Find(turn);
            if (record is not null && (record.Snapshot.Status != ChatTurnStatus.Running || record.ResponderWork is not null))
            {
                return;
            }

            var text = ChatBodies.String(body, "text") ?? string.Empty;
            var context = TurnContexts.For(turn, text, ChatBodies.Context(body));
            if (record is null)
            {
                var current = Current();
                record = new ChatTurnRecord(new ChatTurnSnapshot(turn, commandId, text, ChatTurnStatus.Running, TimeProvider.GetUtcNow()), null);
                var contexts = BoundedList.Append(current.Contexts, context, ChatState.MaxTurns);
                TurnContexts.Retain(contexts);
                await SaveAsync(current with
                {
                    Turns = BoundedList.Append(current.Turns, record, ChatState.MaxTurns),
                    Transcript = BoundedList.Append(current.Transcript, new ChatTurn(true, text), ChatState.MaxTranscript),
                    Contexts = contexts,
                    Version = current.Version + 1,
                }, cancellationToken).ConfigureAwait(true);
            }

            var responder = Responder;
            // Ask carries the question; Turn ignores its body and reads a group-chat journal.
            var outcome = await FireAsync(Signal.Create(AIVocabulary.Ask,
                ChatBodies.Text($"Chat: {Id}\n" + TurnContexts.Preamble(context) + text)),
                responder, new CorrelationId(turn.Value), cancellationToken).ConfigureAwait(true);
            record = record with { ResponderWork = outcome.SignalId };
            await StoreAsync(record, cancellationToken).ConfigureAwait(true);
            if (outcome.Delivered == 0)
            {
                await FailAsync(record, ChatTurnStatus.Failed, $"Responder '{responder}' did not accept the question. Retry with a new message.", cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException error) when (error.CancellationToken == cancellationToken)
        {
            if (Find(turn) is { Snapshot.Status: ChatTurnStatus.Running } running)
            {
                await StoreAsync(running with
                {
                    Snapshot = running.Snapshot with { Status = ChatTurnStatus.Cancelled, SettledAt = TimeProvider.GetUtcNow(), Detail = "cancelled" },
                }, CancellationToken.None).ConfigureAwait(true);
            }

            throw;
        }
    }

    private async Task AnswerAsync(SignalDelivery delivery, JsonNode? body, CancellationToken cancellationToken)
    {
        var record = Find(new SignalId(delivery.CorrelationId.Value));
        if (record is not { Snapshot.Status: ChatTurnStatus.Running })
        {
            return;
        }

        var answer = ChatBodies.String(body, "text") ?? string.Empty;
        var author = delivery.Signal.Type == AIVocabulary.Said
            ? ChatBodies.String(body, "author") ?? delivery.Source.ToString()
            : delivery.Source.ToString();
        record = record with
        {
            Snapshot = record.Snapshot with { Status = ChatTurnStatus.Completed, Answer = answer, Author = author, SettledAt = TimeProvider.GetUtcNow() },
        };
        await StoreAsync(record, cancellationToken, new ChatTurn(false, answer)).ConfigureAwait(true);
        if (answer.Length > 0)
        {
            var response = new Responded(record.Snapshot.Turn, record.Snapshot.CommandId, Id, answer, author, record.Snapshot.Cards);
            await FireAsync(Signal.Create(UIVocabulary.Responded, JsonSerializer.Serialize(response, UIJson.Default.Responded)),
                correlation: delivery.CorrelationId, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task OfferAsync(SignalDelivery delivery, JsonNode? body, CancellationToken cancellationToken)
    {
        var record = Find(new SignalId(delivery.CorrelationId.Value))
            ?? State?.Turns.LastOrDefault(turn => turn.Snapshot.Status == ChatTurnStatus.Running);
        if (record is not { Snapshot.Status: ChatTurnStatus.Running })
        {
            return;
        }

        var kind = delivery.Signal.Type switch
        {
            UIVocabulary.ChartRendered => KitCardKinds.Chart,
            UIVocabulary.GraphRendered => KitCardKinds.Graph,
            UIVocabulary.ImageDescribed => KitCardKinds.Image,
            _ => KitCardKinds.Spreadsheet,
        };
        var card = new KitCardOffer(kind, ChatBodies.String(body, "name") ?? string.Empty, ChatBodies.String(body, "title") ?? string.Empty);
        await StoreAsync(record with { Snapshot = record.Snapshot with { Cards = [.. record.Snapshot.Cards ?? [], card] } }, cancellationToken).ConfigureAwait(true);
    }

    private async Task CancelAsync(SignalId turn, CancellationToken cancellationToken)
    {
        if (Find(turn) is not { Snapshot.Status: ChatTurnStatus.Running } record)
        {
            return;
        }

        if (record.ResponderWork is { } work)
        {
            await GrainFactory.GetGrain<INeuron>(Responder.ToGrainId()).CancelReaction(work).ConfigureAwait(true);
        }

        await FailAsync(record, ChatTurnStatus.Cancelled, "cancelled", cancellationToken).ConfigureAwait(true);
    }

    private async Task FailAsync(ChatTurnRecord record, ChatTurnStatus status, string detail, CancellationToken cancellationToken)
    {
        await StoreAsync(record with
        {
            Snapshot = record.Snapshot with { Status = status, Detail = detail, SettledAt = TimeProvider.GetUtcNow() },
        }, cancellationToken).ConfigureAwait(true);
        await FireAsync(Signal.Create(UIVocabulary.TurnFailed, new JsonObject
        {
            ["turn"] = record.Snapshot.Turn.ToString(),
            ["commandId"] = record.Snapshot.CommandId.ToString(),
            ["detail"] = detail,
        }.ToJsonString()), correlation: new CorrelationId(record.Snapshot.Turn.Value), cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    private ChatTurnRecord? Find(SignalId turn) => State?.Turns.FirstOrDefault(record => record.Snapshot.Turn == turn);

    private ChatState Current() => State ?? new ChatState([], [], [], null, 0);

    private Task StoreAsync(ChatTurnRecord record, CancellationToken cancellationToken, ChatTurn? line = null)
    {
        var current = Current();
        return SaveAsync(current with
        {
            Turns = [.. current.Turns.Select(item => item.Snapshot.Turn == record.Snapshot.Turn ? record : item)],
            Transcript = line is null ? current.Transcript : BoundedList.Append(current.Transcript, line, ChatState.MaxTranscript),
            Version = current.Version + 1,
        }, cancellationToken);
    }

    private static void RequirePayloadSize(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) > Signal.MaxBodyBytes)
        {
            throw new ArgumentException("Keep text and inline context payloads within 64 KiB; use a blob reference for larger context.", nameof(payload));
        }
    }
}
