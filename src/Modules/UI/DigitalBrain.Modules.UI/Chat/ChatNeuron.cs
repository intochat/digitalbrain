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
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.ChatType)]
internal sealed class ChatNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ChatState>> state)
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

            var work = Schedule(Signal.Create(UIVocabulary.TurnRequested, ChatBodies.Requested(arguments, CallerContext.Current())));
            return new Accepted<SignalId>(work, work);
        });

    public Task<Accepted<SignalId>> Cancel(CancelTurn command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("cancel"), command, UIJson.Default.CancelTurn, UIJson.Default.AcceptedSignalId, arguments =>
        {
            _ = Find(arguments.Turn)
                ?? throw new ArgumentException("The turn does not exist. Read the chat turns and cancel a running turn by its turn id.", nameof(command));

            var work = Schedule(Signal.Create(UIVocabulary.TurnCancelling,
                new JsonObject { ["turn"] = arguments.Turn.ToString() }.ToJsonString()));
            return new Accepted<SignalId>(work, work);
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
            case UIVocabulary.TableRendered:
                await OfferAsync(delivery, body, cancellationToken).ConfigureAwait(true);
                break;
            case UIVocabulary.TurnCancelling:
                if (Guid.TryParse(ChatBodies.String(body, "turn"), out var turn) && turn != Guid.Empty)
                {
                    await CancelAsync(new SignalId(turn), delivery, cancellationToken).ConfigureAwait(true);
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

        if (State?.TurnByCommand.Any(item => item.Command == commandId && item.Turn != turn && Find(item.Turn) is not null) == true
            || Find(turn) is not null)
        {
            return;
        }

        var text = ChatBodies.String(body, "text") ?? string.Empty;
        var store = ServiceProvider.GetRequiredService<ITurnContextBlobStore>();
        var current = Current();
        var context = TurnContexts.For(turn, text, ChatBodies.Context(body), current.Contexts);
        var contexts = BoundedList.Append(current.Contexts, context, ChatState.MaxTurns);
        await TurnContexts.RetainAsync(contexts, store, cancellationToken).ConfigureAwait(true);
        var preamble = await TurnContexts.PreambleAsync(context, store, cancellationToken).ConfigureAwait(true);
        var responder = Responder;
        var acceptedAt = TimeProvider.GetUtcNow();
        var work = Announce(Signal.Create(AIVocabulary.Ask, ChatBodies.Text($"Chat: {Id}\n" + preamble + text)),
            responder, new CorrelationId(turn.Value));
        var record = new ChatTurnRecord(new ChatTurnSnapshot(turn, commandId, text, ChatTurnStatus.Running, acceptedAt,
            Context: [.. context.Slots.Select(slot => new ContextDigest(slot.Path, slot.Digest))]), work, responder);
        if (NeuronId.TryParse(ChatBodies.String(body, "caller"), out var caller))
        {
            foreach (var type in new[] { UIVocabulary.Responded, UIVocabulary.TurnFailed, UIVocabulary.CardOffered, UIVocabulary.TurnAccepted })
            {
                await Connect(caller, type).ConfigureAwait(true);
            }
            Announce(Signal.FromJson(UIVocabulary.TurnAccepted, new TurnAccepted(turn, commandId, text, acceptedAt),
                UIJson.Default.TurnAccepted), caller, new CorrelationId(turn.Value));
        }
        await Connect(new NeuronId(UIVocabulary.ActivitiesType, "activities"), UIVocabulary.ActivityExecutionChanged).ConfigureAwait(true);
        AnnounceActivity(record, delivery);
        await SaveAsync(current with
        {
            Turns = BoundedList.Append(current.Turns, record, ChatState.MaxTurns),
            TurnByCommand = BoundedList.Append(current.TurnByCommand, new CommandTurn(commandId, turn), ChatState.MaxCommandTurns),
            Transcript = BoundedList.Append(current.Transcript, new ChatTurn(true, text), ChatState.MaxTranscript),
            Contexts = contexts,
            Version = current.Version + 1,
        }, cancellationToken).ConfigureAwait(true);
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
        await SettleAsync(record, delivery, cancellationToken, new ChatTurn(false, answer)).ConfigureAwait(true);
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
            UIVocabulary.ChartRendered => UiCardKinds.Chart,
            UIVocabulary.GraphRendered => UiCardKinds.Graph,
            UIVocabulary.ImageDescribed => UiCardKinds.Image,
            UIVocabulary.TableRendered => UiCardKinds.Table,
            _ => UiCardKinds.Spreadsheet,
        };
        var card = new UiCardOffer(kind, ChatBodies.String(body, "name") ?? string.Empty, ChatBodies.String(body, "title") ?? string.Empty);
        Announce(Signal.FromJson(UIVocabulary.CardOffered, card, UIJson.Default.UiCardOffer), correlation: new CorrelationId(record.Snapshot.Turn.Value));
        await StoreAsync(record with { Snapshot = record.Snapshot with { Cards = [.. record.Snapshot.Cards ?? [], card] } }, cancellationToken).ConfigureAwait(true);
    }

    private async Task CancelAsync(SignalId turn, SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var record = Find(turn);
        if (record is not { Snapshot.Status: ChatTurnStatus.Running })
        {
            return;
        }

        if (record.ResponderWork is { } work)
        {
            await GrainFactory.GetGrain<INeuron>((record.Responder ?? Responder).ToGrainId()).CancelReaction(work).ConfigureAwait(true);
        }

        await FailAsync(record, ChatTurnStatus.Cancelled, "cancelled", delivery, cancellationToken).ConfigureAwait(true);
    }

    private Task FailAsync(ChatTurnRecord record, ChatTurnStatus status, string detail, SignalDelivery delivery, CancellationToken cancellationToken)
        => SettleAsync(record with
        {
            Snapshot = record.Snapshot with { Status = status, Detail = detail, SettledAt = TimeProvider.GetUtcNow() },
        }, delivery, cancellationToken);

    private async Task SettleAsync(ChatTurnRecord record, SignalDelivery delivery, CancellationToken cancellationToken, ChatTurn? line = null)
    {
        var snapshot = record.Snapshot;
        var announcement = snapshot.Status switch
        {
            ChatTurnStatus.Completed when snapshot.Answer is { Length: > 0 } answer => Signal.Create(
                UIVocabulary.Responded,
                JsonSerializer.Serialize(
                    new Responded(snapshot.Turn, snapshot.CommandId, Id, answer, snapshot.Author ?? string.Empty, snapshot.Cards),
                    UIJson.Default.Responded)),
            ChatTurnStatus.Completed => null,
            _ => Signal.Create(UIVocabulary.TurnFailed, new JsonObject
            {
                ["turn"] = snapshot.Turn.ToString(),
                ["commandId"] = snapshot.CommandId.ToString(),
                ["detail"] = snapshot.Detail,
                ["status"] = snapshot.Status.ToString(),
            }.ToJsonString()),
        };

        if (announcement is not null)
        {
            Announce(announcement, correlation: new CorrelationId(snapshot.Turn.Value));
        }

        AnnounceActivity(record, delivery);
        await StoreAsync(record, cancellationToken, line).ConfigureAwait(true);
    }

    private void AnnounceActivity(ChatTurnRecord record, SignalDelivery delivery)
    {
        var turn = record.Snapshot;
        var phase = turn.Status.ToString().ToLowerInvariant();
        // The turn is a root activity, including when its settling reply was caused by Ask.
        Announce(Signal.FromJson(UIVocabulary.ActivityExecutionChanged,
            new ActivityExecutionChanged(new CorrelationId(turn.Turn.Value), turn.Turn.ToString(), delivery.SignalId,
                null, Id, record.Responder ?? Responder, delivery.Signal.Type, phase, turn.SettledAt ?? turn.StartedAt,
                $"Chat: {turn.Text}", turn.CommandId.ToString(), turn.Detail), UIJson.Default.ActivityExecutionChanged),
            correlation: new CorrelationId(turn.Turn.Value));
    }

    private ChatTurnRecord? Find(SignalId turn) => State?.Turns.FirstOrDefault(record => record.Snapshot.Turn == turn);

    private ChatState Current() => State ?? new ChatState([], [], [], null, 0, []);

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
