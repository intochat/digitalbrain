namespace DigitalBrain.Kernel;

internal static class TimerWork
{
    [ThreadStatic]
    private static List<Task>? _pending;

    internal static void Observe(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _pending?.Add(task);
    }

    internal static CaptureScope Capture(List<Task> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var prior = _pending;
        _pending = sink;
        return new CaptureScope(prior);
    }

    internal readonly struct CaptureScope(List<Task>? prior) : IDisposable
    {
        public void Dispose() => _pending = prior;
    }
}
