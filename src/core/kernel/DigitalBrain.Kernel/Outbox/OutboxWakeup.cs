using DigitalBrain.Abstractions;
using Orleans.Concurrency;

namespace DigitalBrain.Kernel;

[GrainType(GrainTypeName)]
[Reentrant]
internal sealed class OutboxWakeup :
    Grain,
    IOutboxWakeup,
    IRemindable
{
    internal const string GrainTypeName = "db-outbox-wakeup";
    internal const string ReminderName = "db.outbox";
    internal static readonly TimeSpan RetryCadence =
        TimeSpan.FromMinutes(1);

    public async Task Arm()
        => _ = await this.RegisterOrUpdateReminder(ReminderName, RetryCadence, RetryCadence);

    public async Task Disarm()
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

        await GrainFactory
            .GetGrain<IOutboxDrain>(Target().ToGrainId())
            .Drain();
    }

    internal static bool TryParseTarget(string encoded, out NeuronId target)
    {
        target = default;

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        var separator = encoded.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == encoded.Length - 1)
        {
            return false;
        }

        try
        {
            target = NeuronId.FromGrainKey(encoded[..separator], encoded[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private NeuronId Target()
    {
        var encoded = this.GetPrimaryKeyString();
        return TryParseTarget(encoded, out var target)
            ? target
            : throw new InvalidOperationException(
                $"Outbox wakeup key '{encoded}' is not in target-type:owner/name form.");
    }
}
