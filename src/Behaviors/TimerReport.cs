using DigitalBrain.Core;
using DigitalBrain.Time;


public static class TimerReport
{
    public static async Task RunAsync(IDigitalBrain brain, string timerId, Func<TimerElapsed, Task> report, CancellationToken cancellationToken)
    {
        var timer = brain.Get<DigitalBrain.Time.ITimer>(timerId);
        await using var elapsed = await brain.SubscribeAsync<TimerElapsed>(timer, cancellationToken);
        await foreach (var fact in elapsed.ReadAllAsync(cancellationToken))
        {
            await report(fact);
        }
    }
}

