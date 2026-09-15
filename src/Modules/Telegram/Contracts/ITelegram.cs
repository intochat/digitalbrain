using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Telegram;

[Alias("telegram")]
public interface ITelegram : INeuron
{
    [Alias("accept")]
    Task<Accepted<MessageReceived>> Accept(TelegramMessage command);
    [ReadOnly, Alias("read")]
    Task<TelegramSnapshot> Read();
}

[GenerateSerializer, Alias("telegram.message")]
public sealed record TelegramMessage(CommandId Id,
    [property: Id(0)] string EventId, [property: Id(1)] long UserId,
    [property: Id(2)] string Text, [property: Id(3)] long SentUnixSeconds,
    [property: Id(4)] string TimeZone) : Command(Id);

[GenerateSerializer, Alias("telegram.message-received")]
public sealed record MessageReceived(
    [property: Id(0)] string EventId, [property: Id(1)] long UserId,
    [property: Id(2)] string Text, [property: Id(3)] long SentUnixSeconds,
    [property: Id(4)] string TimeZone);

[GenerateSerializer, Alias("telegram.snapshot")]
public sealed record TelegramSnapshot([property: Id(0)] long PublishedCount,
    [property: Id(1)] int RetainedCount, [property: Id(2)] MessageReceived? LastMessage);
