using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class JournalFaultHandle : IAsyncDisposable
{
    private Func<JournalFaultHandle, bool>? _disarm;
    private readonly BrainTestDiagnostics _diagnostics;

    internal JournalFaultHandle(
        JournalFaultRegistration registration, Func<JournalFaultHandle, bool> disarm, BrainTestDiagnostics diagnostics)
    {
        Registration = registration;
        _disarm = disarm;
        _diagnostics = diagnostics;
    }

    internal bool IsConsumed => Registration.Consumed.IsCompletedSuccessfully;

    internal string Message => Registration.Message;

    internal NeuronId Target => Registration.Target;

    internal JournalFaultRegistration Registration { get; }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The public framework control boundary must attach bounded diagnostics while preserving any provider disarm failure as the inner exception.")]
    public ValueTask DisposeAsync()
    {
        try
        {
            _ = Disarm();
            return ValueTask.CompletedTask;
        }
        catch (Exception failure)
        {
            return ValueTask.FromException(_diagnostics.CaptureFailure("fault.dispose", failure));
        }
    }

    internal bool Disarm()
        => Interlocked.Exchange(ref _disarm, null)?.Invoke(this) ?? false;
}
