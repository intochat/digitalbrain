using DigitalBrain.Access;

namespace DigitalBrain.Testing;

internal sealed class TestCallerAmbient : ICallerCarrier
{
    private static readonly AsyncLocal<Caller?> s_current = new();

    public static TestCallerAmbient Instance { get; } = new();

    public Caller? Current => s_current.Value;

    public IDisposable Enter(Caller? caller)
    {
        var previous = s_current.Value;
        s_current.Value = caller;
        return new Restore(previous);
    }

    private sealed class Restore(Caller? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
