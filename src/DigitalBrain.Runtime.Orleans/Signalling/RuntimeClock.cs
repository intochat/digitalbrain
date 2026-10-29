namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>The clock runtime services read for SessionTtl: the silo's <see cref="TimeProvider"/> plus an offset facts move with <c>RuntimeTestHooks.Advance</c>.</summary>
internal sealed class RuntimeClock(TimeProvider time)
{
    private long _offsetTicks;

    public DateTimeOffset Now => time.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    internal void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
}
