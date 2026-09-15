using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GenerateSerializer, Alias("ui.notification-storage-state")]
internal sealed record NotificationStorageState(
    [property: Id(0)] IReadOnlyList<NotificationEntry> Items,
    [property: Id(1)] IReadOnlyList<string> SeenEventIds);

[GrainType(UIVocabulary.NotificationType)]
internal sealed class NotificationNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<NotificationStorageState>> state)
    : Neuron<NotificationStorageState>(runtime, state), INotification
{
    public Task<Accepted<string>> Publish(PublishNotification command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("publish"), command, UIJson.Default.PublishNotification, UIJson.Default.AcceptedString, arguments =>
        {
            Validate(arguments.EventId, 256);
            Validate(arguments.Title, 256);
            Validate(arguments.Message, 16384);
            Validate(arguments.Kind, 64);
            return new Accepted<string>(Id.Name, Schedule(Signal.FromJson(UIVocabulary.NotificationPublishing, arguments, UIJson.Default.PublishNotification)));
        });

    public Task<Accepted<string>> Dismiss(DismissNotification command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("dismiss"), command, UIJson.Default.DismissNotification, UIJson.Default.AcceptedString, arguments =>
        {
            Validate(arguments.EventId, 256);
            return new Accepted<string>(Id.Name, Schedule(Signal.FromJson(UIVocabulary.NotificationDismissing, arguments, UIJson.Default.DismissNotification)));
        });

    [ReadOnly]
    public Task<NotificationState> Read() => Task.FromResult(new NotificationState(State?.Items ?? []));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Source != Id) { return; }
        var current = State ?? new NotificationStorageState([], []);
        NotificationStorageState next;
        switch (delivery.Signal.Type)
        {
            case UIVocabulary.NotificationPublishing:
                if (Body(delivery, UIJson.Default.PublishNotification) is not { } publish) { return; }
                if (current.SeenEventIds.Contains(publish.EventId, StringComparer.Ordinal)) { return; }
                var entry = new NotificationEntry(publish.EventId, publish.Title, publish.Message, publish.Kind,
                    TimeProvider.GetUtcNow().ToUnixTimeSeconds(), false);
                // Inbox retention and retry retention are bounded independently. Old retries beyond
                // the last MaxRememberedEvents unique publications can become new notifications.
                next = new NotificationStorageState(
                    [.. current.Items.Append(entry).TakeLast(NotificationState.MaxItems)],
                    [.. current.SeenEventIds.Append(publish.EventId).TakeLast(NotificationState.MaxRememberedEvents)]);
                break;
            case UIVocabulary.NotificationDismissing:
                if (Body(delivery, UIJson.Default.DismissNotification) is not { } dismiss) { return; }
                next = current with { Items = [.. current.Items.Select(item => item.EventId == dismiss.EventId ? item with { Dismissed = true } : item)] };
                break;
            default:
                return;
        }
        await SaveAsync(next, cancellationToken).ConfigureAwait(true);
    }

    private static void Validate(string value, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > maxLength) { throw new ArgumentException($"Notification field exceeds {maxLength} characters.", nameof(value)); }
    }
}
