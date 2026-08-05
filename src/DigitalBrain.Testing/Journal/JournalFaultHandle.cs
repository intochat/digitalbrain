namespace DigitalBrain.Testing;

public sealed class JournalFaultHandle : IAsyncDisposable
{
    private Func<JournalFaultHandle, bool>? disarm;

    internal JournalFaultHandle(JournalFaultRegistration registration, Func<JournalFaultHandle, bool> disarm)
    {
        Registration = registration;
        this.disarm = disarm;
    }

    public Task Consumed => Registration.Consumed;

    public bool IsConsumed => Registration.Consumed.IsCompletedSuccessfully;

    public NeuronId Target => Registration.Target;

    internal string Message => Registration.Message;

    internal JournalFaultRegistration Registration { get; }

    public ValueTask DisposeAsync()
    {
        _ = Disarm();
        return ValueTask.CompletedTask;
    }

    internal bool Disarm() => Interlocked.Exchange(ref disarm, null)?.Invoke(this) ?? false;
}
