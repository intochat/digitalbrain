using DigitalBrain.Core;
using Orleans.Runtime;
namespace DigitalBrain.Time;

[GrainType("timer")]
internal class TimerNeuron(
    [PersistentState("state", "Default")] IPersistentState<TimerState> state,
    TimeProvider time) : Neuron, ITimer
{
    private bool _unavailable;
    public Task<TimerSnapshot> Read()
    {
        EnsureAvailable();
        return Task.FromResult(state.State.Snapshot());
    }
    public async Task<TimerSnapshot> Schedule(int durationSeconds, string note, long? expectedGeneration = null)
    {
        EnsureAvailable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationSeconds);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        if (expectedGeneration is { } expected && expected != state.State.Generation)
        { throw new InvalidOperationException("Timer generation changed."); }
        if (state.State.Status == TimerStatus.Scheduled)
        { throw new InvalidOperationException("Timer is already scheduled."); }
        var now = time.GetUtcNow();
        var next = new TimerState
        {
            Status = TimerStatus.Scheduled, Generation = checked(state.State.Generation + 1),
            ScheduledAt = now, DueAt = now.AddSeconds(durationSeconds),
            DurationSeconds = durationSeconds, Note = note
        };
        await SaveAsync(next);
        return next.Snapshot();
    }
    public async Task<TimerSnapshot> Stop()
    {
        EnsureAvailable();
        if (state.State.Status != TimerStatus.Scheduled) { return state.State.Snapshot(); }
        await SaveAsync(state.State with { Status = TimerStatus.Cancelled });
        return state.State.Snapshot();
    }
    private async Task SaveAsync(TimerState next)
    {
        state.State = next;
        try { await state.WriteStateAsync(); }
        catch
        {
            try { await state.ReadStateAsync(); }
            catch { _unavailable = true; DeactivateOnIdle(); }
            throw;
        }
    }
    private void EnsureAvailable()
    {
        if (_unavailable) { throw new InvalidOperationException("Timer state is unavailable; activation is stopping."); }
    }
}
