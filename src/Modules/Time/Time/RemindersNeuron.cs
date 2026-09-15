using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Time;

[GrainType("reminders")]
internal sealed class RemindersNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<RemindersSnapshot>> state)
    : Neuron<RemindersSnapshot>(runtime, state), IReminders
{
    private const int Capacity = 1024;
    private const int MaximumFutureSeconds = 366 * 24 * 60 * 60;
    private const string Scheduling = "ReminderScheduling";
    private const string Cancelling = "ReminderCancelling";

    public Task<Accepted<ReminderKey>> Schedule(SetReminder command) => ExecuteCommandAsync(
        Descriptor("schedule"), command, TimeJson.Default.SetReminder, TimeJson.Default.AcceptedReminderKey, arguments =>
        {
            ValidateId(arguments.Id, arguments.ReminderId);
            var body = new ReminderScheduling(arguments.ReminderId, arguments.Text, arguments.DueUnixSeconds,
                TimeProvider.GetUtcNow().ToUnixTimeSeconds());
            return new Accepted<ReminderKey>(new(arguments.ReminderId), Schedule(Signal.FromJson(Scheduling, body, TimeJson.Default.ReminderScheduling)));
        });

    public Task<Accepted<ReminderKey>> Cancel(CancelReminder command) => ExecuteCommandAsync(
        Descriptor("cancel"), command, TimeJson.Default.CancelReminder, TimeJson.Default.AcceptedReminderKey, arguments =>
        {
            ValidateId(arguments.Id, arguments.ReminderId);
            var key = new ReminderKey(arguments.ReminderId);
            return new Accepted<ReminderKey>(key, Schedule(Signal.FromJson(Cancelling, key, TimeJson.Default.ReminderKey)));
        });

    public Task<RemindersSnapshot> Read() => Task.FromResult(State ?? new RemindersSnapshot([]));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var items = State?.Items ?? [];
        if (delivery.Source == Id && delivery.Signal.Type == Scheduling && Body(delivery, TimeJson.Default.ReminderScheduling) is { } reminder)
        {
            var existing = items.FirstOrDefault(item => item.ReminderId == reminder.ReminderId);
            if (existing is not null && existing.Text == reminder.Text && existing.DueUnixSeconds == reminder.DueUnixSeconds)
            {
                return;
            }

            // Validate against admission time, never recovery time: an overdue accepted
            // request still needs its original absolute alarm after an outage.
            var requestedAt = reminder.RequestedAtUnixSeconds ?? delivery.Timestamp.ToUnixTimeSeconds();
            var refusal = existing is not null ? "This reminder ID already exists with different content."
                : string.IsNullOrWhiteSpace(reminder.Text) || reminder.Text.Length > 4096 ? "Provide reminder text between 1 and 4096 characters."
                : reminder.DueUnixSeconds <= requestedAt || reminder.DueUnixSeconds > requestedAt + MaximumFutureSeconds ? "Choose a future reminder time within one year."
                : items.Count >= Capacity ? "This reminder collection has reached its capacity."
                : null;
            if (refusal is not null)
            {
                Announce(Signal.FromJson(TimeSignals.ReminderRejected, new ReminderRejected(reminder.ReminderId, refusal), TimeJson.Default.ReminderRejected));
                await SaveAsync(State ?? new RemindersSnapshot([]), cancellationToken).ConfigureAwait(true);
                return;
            }

            var timerId = TimerId(reminder.ReminderId);
            // External effects precede the one snapshot commit. Both operations are durable and
            // idempotent: replay after a crash cannot allocate a second timer or alarm generation.
            await GrainFactory.GetGrain<INeuronOwnership>(timerId.ToGrainId())
                .ConnectFor(Owner(reminder.ReminderId), Id, TimeSignals.TimerElapsed).ConfigureAwait(true);
            await GrainFactory.GetGrain<ITimer>(timerId.ToGrainId())
                .ScheduleAt(new ScheduleTimerAt(DerivedCommand(reminder.ReminderId, "schedule"), reminder.DueUnixSeconds, reminder.Text)).ConfigureAwait(true);
            await SaveAsync(new RemindersSnapshot([.. items, new(reminder.ReminderId, reminder.Text, reminder.DueUnixSeconds, ReminderStatus.Scheduled)]), cancellationToken).ConfigureAwait(true);
            return;
        }

        if (delivery.Source == Id && delivery.Signal.Type == Cancelling && Body(delivery, TimeJson.Default.ReminderKey) is { } cancelled)
        {
            var item = items.FirstOrDefault(item => item.ReminderId == cancelled.ReminderId);
            if (item is not { Status: ReminderStatus.Scheduled })
            {
                return;
            }
            await GrainFactory.GetGrain<ITimer>(TimerId(item.ReminderId).ToGrainId())
                .Stop(new StopTimer(DerivedCommand(item.ReminderId, "cancel"))).ConfigureAwait(true);
            await Disconnect(item.ReminderId).ConfigureAwait(true);
            await SaveAsync(new RemindersSnapshot([.. items.Select(value => value.ReminderId == item.ReminderId ? value with { Status = ReminderStatus.Cancelled } : value)]), cancellationToken).ConfigureAwait(true);
            return;
        }

        if (delivery.Signal.Type == TimeSignals.TimerElapsed && Body(delivery, TimeJson.Default.TimerElapsedBody) is { Generation: 1 } elapsed)
        {
            var item = items.FirstOrDefault(item => item.Status == ReminderStatus.Scheduled && TimerId(item.ReminderId) == delivery.Source);
            if (item is null || elapsed.Timer != delivery.Source)
            {
                return;
            }
            Announce(Signal.FromJson(TimeSignals.ReminderDue, new ReminderDue(item.ReminderId, item.Text, item.DueUnixSeconds), TimeJson.Default.ReminderDue));
            await Disconnect(item.ReminderId).ConfigureAwait(true);
            await SaveAsync(new RemindersSnapshot([.. items.Select(value => value.ReminderId == item.ReminderId ? value with { Status = ReminderStatus.Delivered } : value)]), cancellationToken).ConfigureAwait(true);
        }
    }

    private Task Disconnect(string reminderId) => GrainFactory.GetGrain<INeuronOwnership>(TimerId(reminderId).ToGrainId())
        .DisconnectFor(Owner(reminderId), Id, TimeSignals.TimerElapsed);

    private NeuronId TimerId(string reminderId) => new("timer", $"reminder-{Hash(reminderId)}");
    private string Owner(string reminderId) => $"reminder-{Hash(reminderId)}";
    private CommandId DerivedCommand(string reminderId, string operation) => new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}\n{reminderId}\n{operation}")).AsSpan(0, 16)));
    private string Hash(string reminderId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}\n{reminderId}")));

    private static void ValidateId(CommandId commandId, string reminderId)
    {
        if (string.IsNullOrWhiteSpace(reminderId) || reminderId.Length > 256)
        {
            throw new CommandRejectedException(commandId, "invalid reminder ID", "Provide between 1 and 256 characters.");
        }
    }
}
