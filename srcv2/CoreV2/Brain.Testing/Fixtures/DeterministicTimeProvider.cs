namespace Brain.Testing.Fixtures;

public sealed class DeterministicTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset _now = initial;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now = _now.Add(amount);
}
