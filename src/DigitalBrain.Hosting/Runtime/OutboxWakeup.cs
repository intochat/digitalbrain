using Orleans.Concurrency;

namespace DigitalBrain;

[Reentrant]
[GrainType(GrainTypeName)]
internal sealed class OutboxWakeup : Grain, IOutboxWakeup, IRemindable
{
    internal const string GrainTypeName = "digitalbrain.outbox-wakeup";
    private const string ReminderName = "db.outbox";

    internal static GrainId AddressOf(NeuronId owner)
        => GrainId.Create(GrainTypeName, NeuronKey.Encode(owner));

    public async Task ArmAsync()
        => _ = await this.RegisterOrUpdateReminder(
            ReminderName,
            DeliveryPolicy.WakeupCadence,
            DeliveryPolicy.WakeupCadence);

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
            throw new InvalidOperationException($"Unknown outbox reminder '{reminderName}'.");
        }

        var owner = NeuronKey.Decode(this.GetPrimaryKeyString());
        await GrainFactory.GetGrain<INeuronHost>(NeuronHost.AddressOf(owner)).DrainAsync();
    }
}
