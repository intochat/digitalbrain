using Orleans.Runtime;

namespace Brain.Kernel;

public static class NeuronReminder
{
    public static string OutboxRecoveryName { get; } = nameof(OutboxRecovery);

    public static Task RegisterOutboxRecoveryAsync(Grain grain) =>
        grain.RegisterOrUpdateReminder(
            OutboxRecoveryName,
            dueTime: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(61));

    public static async Task UnregisterOutboxRecoveryAsync(Grain grain)
    {
        var reminder = await grain.GetReminder(OutboxRecoveryName);
        if (reminder is not null)
            await grain.UnregisterReminder(reminder);
    }

    private static class OutboxRecovery;
}
