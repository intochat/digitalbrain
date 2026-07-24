using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class JournalFaultHandle : IAsyncDisposable
{
    private Func<JournalFaultRegistration, bool>? _disarm;

    internal JournalFaultHandle(
        JournalFaultRegistration registration,
        Func<JournalFaultRegistration, bool> disarm)
    {
        Registration = registration;
        _disarm = disarm;
    }

    internal bool IsConsumed => Registration.Consumed.IsCompletedSuccessfully;

    internal string Message => Registration.Message;

    internal NeuronId Target => Registration.Target;

    internal JournalFaultRegistration Registration { get; }

    public ValueTask DisposeAsync()
    {
        _ = Disarm();
        return ValueTask.CompletedTask;
    }

    internal bool Disarm()
        => Interlocked.Exchange(ref _disarm, null)
            ?.Invoke(Registration)
            ?? false;
}
