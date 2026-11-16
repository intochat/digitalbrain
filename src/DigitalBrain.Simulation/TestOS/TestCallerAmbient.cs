using DigitalBrain.Access;

namespace DigitalBrain.Testing;

internal sealed class TestCallerAmbient : ICallerCarrier
{
    private static readonly AsyncLocal<Caller?> s_current = new();
    private static readonly AsyncLocal<bool> s_inProcess = new();

    public static TestCallerAmbient Instance { get; } = new();

    public Caller? Current => s_current.Value;

    public bool IsKernelActive => s_inProcess.Value && s_current.Value?.Actor is null or Actor.Kernel;

    public IDisposable Enter(Caller? caller)
    {
        var previous = s_current.Value;
        var previousInProcess = s_inProcess.Value;
        s_current.Value = caller;
        if (caller?.Actor is Actor.Kernel or Actor.Module or Actor.Scheduler)
        {
            s_inProcess.Value = true;
        }
        return new Restore(previous, previousInProcess);
    }

    public IDisposable EnterKernelScope(bool clearCaller)
    {
        var previous = s_current.Value;
        var previousInProcess = s_inProcess.Value;
        if (clearCaller)
        {
            s_current.Value = null;
        }
        s_inProcess.Value = true;
        return new Restore(previous, previousInProcess);
    }

    private sealed class Restore(Caller? previous, bool previousInProcess) : IDisposable
    {
        public void Dispose()
        {
            s_current.Value = previous;
            s_inProcess.Value = previousInProcess;
        }
    }
}
