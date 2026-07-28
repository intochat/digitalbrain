using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class ClockContracts(TestingFixture fixture)
{
    private static readonly DateTimeOffset FixedEpoch =
        new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "TestClock starts at the fixed epoch and AdvanceAsync moves UtcNow")]
    public async Task ClockStartsAtFixedEpochAndAdvances()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        Assert.Equal(FixedEpoch, test.Clock.UtcNow);

        await test.Clock.AdvanceAsync(TimeSpan.FromHours(3), cancellationToken);

        Assert.Equal(FixedEpoch + TimeSpan.FromHours(3), test.Clock.UtcNow);
    }

    [Fact(DisplayName = "A later method lease resets the shared clock to the fixed epoch")]
    public async Task NextMethodLeaseResetsClockToFixedEpoch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var first = await fixture.CreateBrainAsync(cancellationToken))
        {
            Assert.Equal(FixedEpoch, first.Clock.UtcNow);
            await first.Clock.AdvanceAsync(TimeSpan.FromDays(9), cancellationToken);
            Assert.Equal(FixedEpoch + TimeSpan.FromDays(9), first.Clock.UtcNow);
        }

        await using var second = await fixture.CreateBrainAsync(cancellationToken);
        Assert.Equal(FixedEpoch, second.Clock.UtcNow);
    }

    [Fact(DisplayName = "TestClock delivers an earlier reminder before a later timer")]
    public async Task ClockDeliversEarlierReminderBeforeLaterTimer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbe>("chronological").Reference;

        await probe.ScheduleAsync(1, 2, reentrantTimer: false, recurringTimer: false);
        await test.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.Equal(["reminder", "timer"], await probe.EventsAsync());
    }

    [Fact(DisplayName = "TestClock delivers an earlier timer before a later reminder")]
    public async Task ClockDeliversEarlierTimerBeforeLaterReminder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbe>("reverse").Reference;

        await probe.ScheduleAsync(2, 1, reentrantTimer: false, recurringTimer: false);
        await test.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.Equal(["timer", "reminder"], await probe.EventsAsync());
    }

    [Fact(DisplayName = "TestClock resolves equal timer and reminder due instants with timer first")]
    public async Task ClockUsesTimerFirstForEqualDueInstants()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbe>("tie").Reference;

        await probe.ScheduleAsync(1, 1, reentrantTimer: false, recurringTimer: false);
        await test.Clock.AdvanceAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.Equal(["timer", "reminder"], await probe.EventsAsync());
    }

    [Fact(DisplayName = "TestClock drains timer work created while delivering mixed due work")]
    public async Task ClockDrainsReentrantTimerWork()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbe>("reentrant").Reference;

        await probe.ScheduleAsync(1, 2, reentrantTimer: true, recurringTimer: false);
        await test.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.Equal(["reminder", "timer", "reentrant-timer"], await probe.EventsAsync());
    }

    [Fact(DisplayName = "TestClock skips a cancelled timer while delivering a due reminder")]
    public async Task ClockSkipsCancelledTimer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbe>("cancellation").Reference;

        await probe.ScheduleAsync(1, 2, reentrantTimer: false, recurringTimer: false);
        await probe.CancelTimerAsync();
        await test.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.Equal(["reminder"], await probe.EventsAsync());
    }

    [Fact(DisplayName = "TestClock keeps its 1024-operation drain bound for recurring timers")]
    public async Task ClockRetainsDrainBound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbe>("bound").Reference;

        await probe.ScheduleAsync(1, 2, reentrantTimer: false, recurringTimer: true);

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => test.Clock.AdvanceAsync(TimeSpan.FromSeconds(1026), cancellationToken));

        Assert.Contains("exceeded 1024 operations", failure.InnerException!.Message, StringComparison.Ordinal);
    }
}

[ClientEntryPoint]
public partial interface IClockProbe : INeuron
{
    Task ScheduleAsync(int reminderDueSeconds, int timerDueSeconds, bool reentrantTimer, bool recurringTimer);

    Task CancelTimerAsync();

    Task<string[]> EventsAsync();
}

internal sealed class ClockProbe : Neuron, IClockProbe, IRemindable
{
    private const string ReminderName = "clock-probe";

    private readonly List<string> _events = [];
    private IGrainReminder? _reminder;
    private ITimer? _timer;
    private bool _reentrantTimer;

    public async Task ScheduleAsync(int reminderDueSeconds, int timerDueSeconds, bool reentrantTimer, bool recurringTimer)
    {
        _events.Clear();
        _reentrantTimer = reentrantTimer;
        _reminder = await this.RegisterOrUpdateReminder(
            ReminderName, TimeSpan.FromSeconds(reminderDueSeconds), TimeSpan.FromDays(1));
        _timer = TimeProvider.CreateTimer(
            static state => ((ClockProbe)state!).OnTimer(),
            this,
            TimeSpan.FromSeconds(timerDueSeconds),
            recurringTimer ? TimeSpan.FromSeconds(1) : Timeout.InfiniteTimeSpan);
    }

    public Task CancelTimerAsync()
    {
        _timer!.Dispose();
        return Task.CompletedTask;
    }

    public Task<string[]> EventsAsync() => Task.FromResult(_events.ToArray());

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        Assert.Equal(ReminderName, reminderName);
        _events.Add("reminder");
        return Task.CompletedTask;
    }

    private void OnTimer()
    {
        _events.Add("timer");

        if (_reentrantTimer)
        {
            _reentrantTimer = false;
            TimeProvider.CreateTimer(
                static state => ((ClockProbe)state!).OnReentrantTimer(),
                this,
                TimeSpan.Zero,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void OnReentrantTimer() => _events.Add("reentrant-timer");
}
