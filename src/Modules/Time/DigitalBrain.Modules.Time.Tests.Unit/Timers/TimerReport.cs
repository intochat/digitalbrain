using DigitalBrain.Contracts;
using DigitalBrain.Time.Timers;
using DigitalBrain.Time.Timers.Signals;

public static class TimerReport
{
    public static async Task RunAsync(IDigitalBrain brain, string timerId, Func<TimerTick, Task> report, CancellationToken cancellationToken)
    {
        var timer = brain.Get<DigitalBrain.Time.Timers.ITimer>(timerId);
        await using var elapsed = await brain.SubscribeAsync<TimerTick>(timer, cancellationToken);
        await foreach (var fact in elapsed.ReadAllAsync(cancellationToken))
        {
            await report(fact);
        }
    }
}
