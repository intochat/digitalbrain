namespace DigitalBrain.Kernel;

internal static class NeuronTime
{
    internal static object ServiceKey { get; } = new();
}

internal interface INeuronTimerRegistrationSource
{
    ITimer CreateTimer(
        GrainId registrationOwner,
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period);
}

internal sealed class NeuronTimeProvider(
    TimeProvider provider,
    GrainId registrationOwner) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone
        => provider.LocalTimeZone;

    public override long TimestampFrequency
        => provider.TimestampFrequency;

    public override DateTimeOffset GetUtcNow()
        => provider.GetUtcNow();

    public override long GetTimestamp()
        => provider.GetTimestamp();

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
        => provider is INeuronTimerRegistrationSource registrations
            ? registrations.CreateTimer(
                registrationOwner,
                callback,
                state,
                dueTime,
                period)
            : provider.CreateTimer(callback, state, dueTime, period);
}
