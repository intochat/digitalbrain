namespace DigitalBrain.Testing;

// The test-facing half of an armed commit fault: await Consumed for the deterministic
// "the fault fired" sync point; dispose to disarm a sticky or no-longer-wanted fault. A
// handle that is neither consumed nor disarmed when its test ends is leaked test intent —
// DigitalBrainTest fails the test at dispose naming it.
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
