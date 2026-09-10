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
            // Backstops the kernel's bound of 1024 resolved commands without suppressing this entry's retries.
            if (State?.TurnByCommand.Any(item => item.Command == commandId && item.Turn != turn && Find(item.Turn) is not null) == true)
            {
                return;
            }

            var record = Find(turn);
            if (record is not null && record.Snapshot.Status != ChatTurnStatus.Running)
            {
                if (!record.SettlementFired)
                {
                    await AnnounceAsync(record, cancellationToken).ConfigureAwait(true);
                }

                return;
            }

            if (record?.ResponderWork is not null)
            {
                return;
            }

            var text = ChatBodies.String(body, "text") ?? string.Empty;
            var store = ServiceProvider.GetRequiredService<ITurnContextBlobStore>();
            // A retry reuses the context the first attempt built, so the digests a caller already read stay put.
            var context = State?.Contexts.FirstOrDefault(stored => stored.Turn == turn)
                ?? TurnContexts.For(turn, text, ChatBodies.Context(body), State?.Contexts ?? []);
            if (record is null)
            {
                var current = Current();
                record = new ChatTurnRecord(new ChatTurnSnapshot(turn, commandId, text, ChatTurnStatus.Running, TimeProvider.GetUtcNow(),
                    Context: [.. context.Slots.Select(slot => new ContextDigest(slot.Path, slot.Digest))]), null, false);
                var contexts = BoundedList.Append(current.Contexts, context, ChatState.MaxTurns);
                await TurnContexts.RetainAsync(contexts, store, cancellationToken).ConfigureAwait(true);
                await SaveAsync(current with
                {
                    Turns = BoundedList.Append(current.Turns, record, ChatState.MaxTurns),
                    TurnByCommand = BoundedList.Append(current.TurnByCommand, new CommandTurn(commandId, turn), ChatState.MaxCommandTurns),
                    Transcript = BoundedList.Append(current.Transcript, new ChatTurn(true, text), ChatState.MaxTranscript),
                    Contexts = contexts,
                    Version = current.Version + 1,
                }, cancellationToken).ConfigureAwait(true);
            }

            var preamble = await TurnContexts.PreambleAsync(context, store, cancellationToken).ConfigureAwait(true);
            var responder = Responder;
            // Ask carries the question; Turn ignores its body and reads a group-chat journal.
            var outcome = await FireAsync(Signal.Create(AIVocabulary.Ask,
                ChatBodies.Text($"Chat: {Id}\n" + preamble + text)),
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
                    // The edge cancelled this reaction and already knows the outcome; the kernel will not retry it to announce.
                    SettlementFired = true,
                }, CancellationToken.None).ConfigureAwait(true);
            }

            throw;
        }
    }

    private async Task AnswerAsync(SignalDelivery delivery, JsonNode? body, CancellationToken cancellationToken)
    {
        var record = Find(new SignalId(delivery.CorrelationId.Value));
        if (record is null)
        {
            return;
        }

        if (record.Snapshot.Status == ChatTurnStatus.Running)
        {
            var answer = ChatBodies.String(body, "text") ?? string.Empty;
            var author = delivery.Signal.Type == AIVocabulary.Said
                ? ChatBodies.String(body, "author") ?? delivery.Source.ToString()
                : delivery.Source.ToString();
            record = record with
            {
                Snapshot = record.Snapshot with { Status = ChatTurnStatus.Completed, Answer = answer, Author = author, SettledAt = TimeProvider.GetUtcNow() },
            };
            await AnnounceAsync(record, cancellationToken, new ChatTurn(false, answer)).ConfigureAwait(true);
            return;
        }
        else if (record.SettlementFired || record.Snapshot.Status != ChatTurnStatus.Completed)
        {
            return;
        }

        await AnnounceAsync(record, cancellationToken).ConfigureAwait(true);
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
        var record = Find(turn);
        if (record is null || (record.Snapshot.Status != ChatTurnStatus.Running && record.SettlementFired))
        {
            return;
        }

        if (record.Snapshot.Status == ChatTurnStatus.Running && record.ResponderWork is { } work)
        {
            await GrainFactory.GetGrain<INeuron>(Responder.ToGrainId()).CancelReaction(work).ConfigureAwait(true);
        }

        await FailAsync(record, ChatTurnStatus.Cancelled, "cancelled", cancellationToken).ConfigureAwait(true);
    }

    private async Task FailAsync(ChatTurnRecord record, ChatTurnStatus status, string detail, CancellationToken cancellationToken)
    {
        if (record.Snapshot.Status == ChatTurnStatus.Running)
        {
            record = record with
            {
                Snapshot = record.Snapshot with { Status = status, Detail = detail, SettledAt = TimeProvider.GetUtcNow() },
            };
        }
        else if (record.SettlementFired)
        {
            return;
        }

        await AnnounceAsync(record, cancellationToken).ConfigureAwait(true);
    }

    private async Task AnnounceAsync(ChatTurnRecord record, CancellationToken cancellationToken, ChatTurn? line = null)
    {
        var snapshot = record.Snapshot;
        // A completed turn with no answer has nothing to announce, but still needs the marker or it retries forever.
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
            }.ToJsonString()),
        };

        if (announcement is not null)
        {
            ServiceProvider.GetService<IChatSettlementCrashPoint>()?.BeforeSettlementFire(snapshot.Turn);
            Announce(announcement, correlation: new CorrelationId(snapshot.Turn.Value));
        }

        await StoreAsync(record with { SettlementFired = true }, cancellationToken, line).ConfigureAwait(true);
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
