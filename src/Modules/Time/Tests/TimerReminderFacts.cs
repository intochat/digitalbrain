using DigitalBrain.Time;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class TimerReminderFacts
{
    [Fact]
    public async Task ActualReminderCommitsElapsedAndPublishesTypedFact()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TimerTestSupport.StartAsync(ct);
        var timer = host.Brain.Get<ITimer>("real");
        await using var subscription = await host.Brain.SubscribeAsync<TimerElapsed>(timer, ct);
        var next = FirstAsync(subscription.ReadAllAsync(ct), ct);
        var scheduled = await timer.Schedule(1, "tea");
        var fact = await next.WaitAsync(TimeSpan.FromSeconds(90), ct);
        Assert.Equal(scheduled.Generation, fact.Generation);
        Assert.Equal("real", fact.TimerId);
        Assert.Equal(TimerStatus.Elapsed, (await timer.Read()).Status);
    }

    [Fact]
    public async Task FreshHostRecoversTheScheduleAndActualReminder()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "brain-time-" + Guid.NewGuid().ToString("N"));
        try
        {
            TimerSnapshot scheduled;
            await using (var first = await TimerTestSupport.StartAsync(ct, directory))
            {
                scheduled = await first.Brain.Get<ITimer>("restart").Schedule(3, "tea");
            }
            await using var second = await TimerTestSupport.StartAsync(ct, directory);
            var timer = second.Brain.Get<ITimer>("restart");
            var elapsed = await TestWait.UntilAsync(_ => timer.Read(), snapshot => snapshot.Status == TimerStatus.Elapsed,
                TimeSpan.FromSeconds(90), ct);
            Assert.Equal(scheduled.Generation, elapsed.Generation);
            Assert.Equal(scheduled.DueAt, elapsed.DueAt);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MissingReminderConfigurationFailsStartup()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var host = await BrainTestHost.StartAsync(new() { ConfigureSilo = silo => silo.AddTime() }, TestContext.Current.CancellationToken);
        });
        Assert.Contains("reminder", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<TimerElapsed> FirstAsync(IAsyncEnumerable<TimerElapsed> facts, CancellationToken ct)
    {
        await foreach (var fact in facts.WithCancellation(ct)) { return fact; }
        throw new InvalidOperationException("Subscription ended without a fact.");
    }
}
