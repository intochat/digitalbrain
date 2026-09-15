using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[Alias("ui.notification")]
public interface INotification : INeuron
{
    [Alias("publish")]
    Task<Accepted<string>> Publish(PublishNotification command, CancellationToken cancellationToken = default);

    [Alias("dismiss")]
    Task<Accepted<string>> Dismiss(DismissNotification command, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("read")]
    Task<NotificationState> Read();
}

[GenerateSerializer, Alias("ui.publish-notification")]
public sealed record PublishNotification(CommandId Id,
    [property: Id(0)] string EventId,
    [property: Id(1)] string Title,
    [property: Id(2)] string Message,
    [property: Id(3)] string Kind) : Command(Id);

[GenerateSerializer, Alias("ui.dismiss-notification")]
public sealed record DismissNotification(CommandId Id, [property: Id(0)] string EventId) : Command(Id);

[GenerateSerializer, Alias("ui.notification-state")]
public sealed record NotificationState([property: Id(0)] IReadOnlyList<NotificationEntry> Items)
{
    public const int MaxItems = 512;
    public const int MaxRememberedEvents = 4096;
}

[GenerateSerializer, Alias("ui.notification-entry")]
public sealed record NotificationEntry(
    [property: Id(0)] string EventId,
    [property: Id(1)] string Title,
    [property: Id(2)] string Message,
    [property: Id(3)] string Kind,
    [property: Id(4)] long CreatedUnixSeconds,
    [property: Id(5)] bool Dismissed);
