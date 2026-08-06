using Orleans.Concurrency;

namespace DigitalBrain;

internal sealed record DeliveryEnvelope(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp,
    SynapseRef? Cause)
{
    internal SynapseMetadata Metadata => new(Source, Sequence, Timestamp);
}

internal static class DeliveryPolicy
{
    internal const int MaximumAttempts = 1000;
    internal static readonly TimeSpan RetryHorizon = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);
    internal static readonly TimeSpan WakeupCadence = TimeSpan.FromMinutes(1);
}

[Alias("db.wakeup")]
internal interface IOutboxWakeup : IGrainWithStringKey
{
    [Alias("arm")]
    Task ArmAsync();

    [Alias("disarm")]
    Task DisarmAsync();
}

[Reentrant]
[GrainType(GrainTypeName)]
internal sealed class OutboxWakeup : Grain, IOutboxWakeup, IRemindable
{
    internal const string GrainTypeName = "digitalbrain.outbox-wakeup";
    private const string ReminderName = "db.outbox";

    internal static GrainId AddressOf(NeuronId owner) => GrainId.Create(GrainTypeName, NeuronKey.Encode(owner));

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
            throw new InvalidOperationException($"Unknown outbox reminder '{reminderName}'.");
        }

        await GrainFactory.GetGrain<Neuron.IDrainEntry>(Target()).DrainAsync();
    }

    private GrainId Target()
    {
        var owner = NeuronKey.Decode(this.GetPrimaryKeyString());
        return GrainId.Create(owner.Kind, owner.Name);
    }
}
