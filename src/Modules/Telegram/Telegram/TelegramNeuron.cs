using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Telegram;

[GrainType("telegram")]
internal sealed class TelegramNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TelegramState>> state)
    : Neuron<TelegramState>(runtime, state), ITelegram
{
    private const string Receiving = "TelegramReceiving";

    public Task<Accepted<MessageReceived>> Accept(TelegramMessage command) => ExecuteCommandAsync(
        Descriptor("accept"), command, TelegramJson.Default.TelegramMessage, TelegramJson.Default.AcceptedMessageReceived, arguments =>
        {
            if (arguments.UserId <= 0 || arguments.UserId.ToString(CultureInfo.InvariantCulture) != Id.Name ||
                string.IsNullOrWhiteSpace(arguments.EventId) || arguments.EventId.Length > 256 ||
                string.IsNullOrWhiteSpace(arguments.Text) || arguments.Text.Length > 4096 || arguments.SentUnixSeconds is <= 0 or > 253402300799 ||
                !TelegramOptions.IsTimeZoneValid(arguments.TimeZone))
            {
                throw new CommandRejectedException(arguments.Id, "invalid Telegram receipt or user scope", "Use the verified private sender and a valid message.");
            }
            var body = new MessageReceived(arguments.EventId, arguments.UserId, arguments.Text, arguments.SentUnixSeconds, arguments.TimeZone);
            return new Accepted<MessageReceived>(body, Schedule(Signal.FromJson(Receiving, body, TelegramJson.Default.MessageReceived)));
        });

    public Task<TelegramSnapshot> Read() => Task.FromResult(new TelegramSnapshot(State?.PublishedCount ?? 0, State?.EventIds.Length ?? 0, State?.LastMessage));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Source != Id || delivery.Signal.Type != Receiving || Body(delivery, TelegramJson.Default.MessageReceived) is not { } body ||
            body.UserId.ToString(CultureInfo.InvariantCulture) != Id.Name)
        {
            return;
        }
        var retained = State?.EventIds ?? [];
        if (retained.Contains(body.EventId, StringComparer.Ordinal)) { return; }
        var next = (retained.Length >= 4096 ? retained[1..] : retained).Append(body.EventId).ToArray();
        Announce(Signal.FromJson(TelegramModule.MessageReceivedSignal, body, TelegramJson.Default.MessageReceived));
        await SaveAsync(new TelegramState(next, (State?.PublishedCount ?? 0) + 1, body), cancellationToken).ConfigureAwait(true);
    }
}

[GenerateSerializer, Alias("telegram.state")]
internal sealed record TelegramState([property: Id(0)] string[] EventIds,
    [property: Id(1)] long PublishedCount, [property: Id(2)] MessageReceived LastMessage);
