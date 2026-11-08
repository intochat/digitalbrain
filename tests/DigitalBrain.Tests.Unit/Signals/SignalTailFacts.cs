using DigitalBrain.Spi;

namespace DigitalBrain.Tests.Unit.Signals;

public sealed class SignalTailFacts
{
    private static readonly NeuronId Publisher = NeuronId.Parse("b1/tail/a");

    [Fact]
    public async Task AnIdleTailBacksOffToOneSecondAndResetsAfterSignals()
    {
        var reads = 0;
        List<TimeSpan> waits = [];
        List<long> seen = [];
        using var stop = new CancellationTokenSource();

        // Read 7 finds one signal; every other read is empty.
        Task<SignalPage> Read(long since, int limit, CancellationToken ct)
        {
            IReadOnlyList<JournaledSignal> held = ++reads == 7 ? [new JournaledSignal(Publisher, 1, new StateChanged("n", "c"), DateTimeOffset.UnixEpoch)] : [];
            return Task.FromResult(new SignalPage(0, 1, held));
        }

        Task Delay(TimeSpan wait, CancellationToken ct)
        {
            waits.Add(wait);
            if (waits.Count == 8) { stop.Cancel(); }
            return Task.CompletedTask;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in SignalTail.Poll(Read, 0, SignalTail.PollInterval, SignalTail.MaxPollInterval, stop.Token, delay: Delay))
            {
                seen.Add(item.Sequence);
            }
        });

        Assert.Equal([1L], seen);
        Assert.Equal([50d, 100, 200, 400, 800, 1000, 50, 100], waits.Select(w => w.TotalMilliseconds));
    }
}
