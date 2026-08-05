using Orleans.Concurrency;

namespace DigitalBrain;

[Alias("db.wakeup")]
internal interface IOutboxWakeup : IGrainWithStringKey
{
    [Alias("arm")]
    Task ArmAsync();

    [Alias("disarm")]
    Task DisarmAsync();
}

// The durable reminder backstop (v1 port): keyed by the target neuron's own "kind/name"
// key, one reminder per neuron, WakeupCadence apart. Arm rides the arm-before-commit leg
// of every deliverable turn; the neuron disarms itself once its backlog, ask pins and
// schedule table are all empty (the ForgetWakeUpWhenOutboxIsEmpty pattern). Reentrant:
// Arm/Disarm racing a reminder tick is harmless — a spurious tick against a settled
// neuron drains nothing and re-disarms.
[Reentrant]
internal sealed class OutboxWakeup : Grain, IOutboxWakeup, IRemindable
{
    internal const string ReminderName = "db.outbox";

    public async Task ArmAsync()
        => _ = await this.RegisterOrUpdateReminder(
            ReminderName, DeliveryPolicy.WakeupCadence, DeliveryPolicy.WakeupCadence);

    public async Task DisarmAsync()
    {
        if (await this.GetReminder(ReminderName) is { } reminder)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, ReminderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Outbox wakeup '{this.GetPrimaryKeyString()}' does not own reminder '{reminderName}'.");
        }

        await GrainFactory.GetGrain<Neuron.IDrainEntry>(Target()).DrainAsync();
    }

    private GrainId Target()
    {
        var encoded = this.GetPrimaryKeyString();
        var separator = encoded.IndexOf('/', StringComparison.Ordinal);
        return separator > 0 && separator < encoded.Length - 1
            ? GrainId.Create(encoded[..separator], encoded[(separator + 1)..])
            : throw new InvalidOperationException($"Outbox wakeup key '{encoded}' is not in kind/name form.");
    }
}
