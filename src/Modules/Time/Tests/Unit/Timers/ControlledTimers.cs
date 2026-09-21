using System.Collections.Concurrent;
using Orleans.Runtime;
using Orleans.Timers;

namespace DigitalBrain.Tests;

// Controls delivery, not virtual time. Every callback still executes on a real Orleans turn.
internal sealed class ControlledTimers : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => UtcNow;
    private readonly ConcurrentDictionary<GrainId, ControlledTimer> _timers = new();
    public ControlledTimer For(GrainId id) => _timers[id];
    public void Add(GrainId id, ControlledTimer timer) => _timers[id] = timer;
}

internal sealed class ControlledTimer(GrainTimerCreationOptions options, IGrainContext context,
    Func<CancellationToken, Task> callback) : IGrainTimer
{
    public IGrainContext Context { get; } = context;
    public GrainTimerCreationOptions Options { get; } = options;
    public IGrainTimer Inner { get; set; } = null!;
    public bool IsDisposed { get; private set; }
    private TaskCompletionSource? _fired;

    public async Task FireAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Context.Scheduler.QueueAction(() =>
        {
            try
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                _fired = completion;
                Inner.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    // Deliberately inject an obsolete callback, bypassing native disposal, on the grain scheduler.
    public async Task DeliverObsoleteAsync(CancellationToken ct)
    {
        var invocation = new Task<Task>(() => callback(CancellationToken.None));
        Context.Scheduler.QueueTask(invocation);
        await invocation.Unwrap().WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    public async Task DeliverAsync(Func<Task> callback)
    {
        try { await callback(); _fired!.TrySetResult(); }
        catch (Exception error) { _fired!.TrySetException(error); }
    }

    public void Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("Tests explicitly control firing.");
    public void Dispose() { IsDisposed = true; Inner.Dispose(); }
}

internal sealed class ControlledTimerRegistry(ITimerRegistry inner, ControlledTimers timers) : ITimerRegistry
{
    public IGrainTimer RegisterGrainTimer<TState>(IGrainContext context, Func<TState, CancellationToken, Task> callback,
        TState state, GrainTimerCreationOptions options)
    {
        if (context.GrainId.Type.ToString() != "timer")
        { return inner.RegisterGrainTimer(context, callback, state, options); }
        var timer = new ControlledTimer(options, context, ct => callback(state, ct));
        var controlled = options with { DueTime = Timeout.InfiniteTimeSpan, Period = Timeout.InfiniteTimeSpan };
        timer.Inner = inner.RegisterGrainTimer(context, (value, ct) => timer.DeliverAsync(() => callback(value, ct)), state, controlled);
        timers.Add(context.GrainId, timer);
        return timer;
    }

    [Obsolete("Use RegisterGrainTimer.")]
    public IDisposable RegisterTimer(IGrainContext context, Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period)
        => inner.RegisterTimer(context, callback, state, dueTime, period);
}
