using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class ClockAndFaultContracts(TestingFixture fixture)
{
    private static readonly DateTimeOffset FixedEpoch =
        new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClockStartsFixedAndAdvancesWithoutWallTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        Assert.Equal(FixedEpoch, test.Clock.UtcNow);

        var stopwatch = Stopwatch.StartNew();
        await test.Clock.AdvanceAsync(TimeSpan.FromDays(7), cancellationToken);

        Assert.Equal(FixedEpoch + TimeSpan.FromDays(7), test.Clock.UtcNow);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ClockRejectsNegativeAdvances()
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => test.Clock.AdvanceAsync(
                TimeSpan.FromTicks(-1),
                TestContext.Current.CancellationToken));
        Assert.IsType<ArgumentOutOfRangeException>(
            failure.InnerException);
    }

    [Fact]
    public async Task ANewlyActivatedNeuronObservesTheAdvancedInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var expected = FixedEpoch + TimeSpan.FromDays(2);

        await test.Clock.AdvanceAsync(TimeSpan.FromDays(2), cancellationToken);

        var probe = test.Neuron<IClockProbeNeuron>("activated-after-advance");
        await probe.Reference.ObserveTime();
        var observed =
            Assert.Single(await probe.Outgoing.ReadAsync<TimeObserved>(
                cancellationToken: cancellationToken));

        Assert.Equal(expected, observed.Synapse.UtcNow);
        Assert.Equal(expected, observed.Timestamp);
    }

    [Fact]
    public async Task ClockFiresAOneShotTimerOnlyAtItsExactDueInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("one-shot");

        await probe.Reference.ArmTimer("due", TimeSpan.FromHours(1));
        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(59), cancellationToken);

        Assert.Empty(await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(1), cancellationToken);

        var fired = Assert.Single(await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken));
        Assert.Equal("due", fired.Synapse.Value);
        Assert.Equal(FixedEpoch + TimeSpan.FromHours(1), fired.Synapse.UtcNow);
        Assert.Equal(fired.Synapse.UtcNow, fired.Timestamp);

        await test.Clock.AdvanceAsync(TimeSpan.FromDays(1), cancellationToken);

        Assert.Single(await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task AdvanceWaitsForSynchronouslyCompletedSelfProxyWork()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("fire-and-forget");

        await probe.Reference.ArmSynchronouslyCompletedTimer(
            "queued",
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);

        var fired = Assert.Single(await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken));
        Assert.Equal("queued", fired.Synapse.Value);
        Assert.Equal(FixedEpoch + TimeSpan.FromHours(1), fired.Timestamp);
    }

    [Fact]
    public async Task EqualDueTimersFireInRegistrationOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("stable-order");

        await probe.Reference.ArmEqualTimers(
            "first",
            "second",
            TimeSpan.FromMinutes(5));
        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(5), cancellationToken);

        var fired = await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken);
        Assert.Equal(["first", "second"], fired.Select(item => item.Synapse.Value));
    }

    [Fact]
    public async Task PeriodicTimersAdvanceFromTheirPriorDueInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("periodic");

        await probe.Reference.ArmPeriodicTimer(
            "periodic",
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1));
        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(3),
            cancellationToken);

        var fired = await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken);
        Assert.Equal(
            [
                FixedEpoch + TimeSpan.FromHours(1),
                FixedEpoch + TimeSpan.FromHours(2),
                FixedEpoch + TimeSpan.FromHours(3),
            ],
            fired.Select(item => item.Synapse.UtcNow));
    }

    [Fact]
    public async Task TimerChangeAndBothDisposeFormsAreHonored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("timer-mutations");

        await probe.Reference.ArmTimer("changed", TimeSpan.FromHours(1));
        await probe.Reference.ChangeTimer(
            "changed",
            TimeSpan.FromHours(2),
            Timeout.InfiniteTimeSpan);
        await probe.Reference.ArmTimer("disposed", TimeSpan.FromHours(1));
        await probe.Reference.DisposeTimer("disposed");
        await probe.Reference.ArmTimer(
            "async-disposed",
            TimeSpan.FromHours(1));
        await probe.Reference.DisposeTimerAwaited("async-disposed");

        await test.Clock.AdvanceAsync(TimeSpan.FromHours(1), cancellationToken);
        Assert.Empty(await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(TimeSpan.FromHours(1), cancellationToken);
        var fired = Assert.Single(await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken));
        Assert.Equal("changed", fired.Synapse.Value);
    }

    [Fact]
    public async Task ConcurrentAdvancesAreSerializedFromThePriorTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("concurrent-advance");

        await probe.Reference.ArmTimer(
            "yield-first-advance",
            TimeSpan.FromMinutes(1));

        var first = test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        var second = test.Clock.AdvanceAsync(
            TimeSpan.FromHours(2),
            cancellationToken);

        await Task.WhenAll(first, second);

        Assert.Equal(
            FixedEpoch + TimeSpan.FromHours(3),
            test.Clock.UtcNow);
        Assert.Single(await probe.Outgoing.ReadAsync<TimerFired>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task DueRemindersAreDeliveredOnlyByClockAdvance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("reminder");

        await probe.Reference.ArmReminder(TimeSpan.FromHours(1));
        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(59), cancellationToken);

        Assert.Empty(await probe.Outgoing.ReadAsync<ReminderFired>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(1), cancellationToken);

        var fired = Assert.Single(await probe.Outgoing.ReadAsync<ReminderFired>(
            cancellationToken: cancellationToken));
        Assert.Equal(FixedEpoch + TimeSpan.FromHours(1), fired.Synapse.UtcNow);
        Assert.Equal(
            (FixedEpoch + TimeSpan.FromHours(1)).UtcDateTime,
            fired.Synapse.FirstTickTime);
        Assert.Equal(TimeSpan.FromHours(1), fired.Synapse.Period);
        Assert.Equal(
            fired.Synapse.UtcNow.UtcDateTime,
            fired.Synapse.CurrentTickTime);
        Assert.Equal(fired.Synapse.UtcNow, fired.Timestamp);
    }

    [Fact]
    public async Task EachMethodResetsTimeAndDisablesPriorTimers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        IClockProbeNeuron priorProbe;

        await using (var first = await fixture.CreateBrainAsync(cancellationToken))
        {
            var probe = first.Neuron<IClockProbeNeuron>("prior-method");
            priorProbe = probe.Reference;
            await priorProbe.ResetDiagnostics();
            await priorProbe.ArmTimer("prior-timer", TimeSpan.FromHours(1));
            await first.Clock.AdvanceAsync(TimeSpan.FromMinutes(10), cancellationToken);
        }

        await using var second = await fixture.CreateBrainAsync(cancellationToken);

        Assert.Equal(FixedEpoch, second.Clock.UtcNow);

        await second.Clock.AdvanceAsync(TimeSpan.FromHours(1), cancellationToken);

        var diagnostics = await priorProbe.ReadDiagnostics();
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task PriorMethodRemindersCannotFireInTheNextMethod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        IClockProbeNeuron priorProbe;

        await using (var first = await fixture.CreateBrainAsync(cancellationToken))
        {
            var probe = first.Neuron<IClockProbeNeuron>("prior-reminder");
            priorProbe = probe.Reference;
            await priorProbe.ResetDiagnostics();
            await priorProbe.ArmReminder(TimeSpan.FromHours(1));
            await first.Clock.AdvanceAsync(TimeSpan.FromMinutes(10), cancellationToken);
        }

        await using var second = await fixture.CreateBrainAsync(cancellationToken);
        await second.Clock.AdvanceAsync(TimeSpan.FromHours(1), cancellationToken);

        var diagnostics = await priorProbe.ReadDiagnostics();
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task PriorMethodClockCannotMutateTheNextMethod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        TestClock priorClock;

        await using (var first = await fixture.CreateBrainAsync(cancellationToken))
        {
            priorClock = first.Clock;
        }

        await using var second = await fixture.CreateBrainAsync(cancellationToken);

        Assert.Throws<ObjectDisposedException>(() => priorClock.UtcNow);
        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => priorClock.AdvanceAsync(
                TimeSpan.FromHours(1),
                cancellationToken));
        Assert.IsType<ObjectDisposedException>(failure.InnerException);
        Assert.Equal(FixedEpoch, second.Clock.UtcNow);
    }

    [Fact]
    public async Task StaleReminderHandleCannotUnregisterItsReplacement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IClockProbeNeuron>("stale-reminder");

        await probe.Reference.ArmAndRefreshReminder();

        var failure = await Assert.ThrowsAsync<ReminderException>(
            probe.Reference.UnregisterStaleReminder);
        Assert.Contains(ReminderName, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultIsScopedToExactlyOneTestNeuron()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var target = test.Neuron<IEchoNeuron>("fault-target");
        var other = test.Neuron<IEchoNeuron>("fault-other");
        await using var fault =
            target.FailNextJournalCommit("target commit failure");

        await other.Reference.Publish("other");

        var observed = Assert.Single(await other.Outgoing.ReadAsync<Echoed>(
            cancellationToken: cancellationToken));
        Assert.Equal("other", observed.Synapse.Value);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => target.Reference.Publish("target"));
        Assert.Equal("target commit failure", failure.Message);
    }

    [Fact]
    public async Task FaultAfterCompletedWritesThrowsTheExactMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("fault-after");
        await using var fault =
            echo.FailJournalCommitAfter(1, "expected commit failure");

        await echo.Reference.Publish("committed");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => echo.Reference.Publish("failed"));
        Assert.Equal("expected commit failure", failure.Message);
    }

    [Fact]
    public async Task FailedCapabilityEntryCommitDoesNotRemainInTheIncomingJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var target = test.Neuron<IEchoNeuron>("capability-entry-fault");
        var driver = test.Neuron<ICapabilityRetryDriver>("capability-entry-driver");
        await using var fault =
            target.FailNextJournalCommit("capability entry commit failure");

        await driver.Reference.PublishWithRetry(target.Id, "committed once");

        var request = Assert.Single(await target.Incoming.ReadAsync<CapabilityRequested>(
            cancellationToken: cancellationToken));
        Assert.Equal(nameof(IEchoNeuron.Publish), request.Synapse.Method);
        Assert.Equal(
            "committed once",
            Assert.Single(await target.Outgoing.ReadAsync<Echoed>(
                cancellationToken: cancellationToken)).Synapse.Value);
    }

    [Fact]
    public async Task FaultLeftUnconsumedAndUndisposedFailsMethodCleanup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("fault-leaked");
        _ = echo.FailNextJournalCommit("unconsumed commit failure");

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            async () => await test.DisposeAsync());
        var cleanup = Assert.IsType<InvalidOperationException>(
            failure.InnerException);

        Assert.Contains(echo.Id.ToString(), cleanup.Message, StringComparison.Ordinal);
        Assert.Contains(
            "unconsumed commit failure",
            cleanup.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultExplicitlyDisposedIsDisarmedBeforeCleanup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("fault-disarmed");
        var fault = echo.FailNextJournalCommit("must not remain armed");

        await fault.DisposeAsync();
        await echo.Reference.Publish("committed");

        var observed = Assert.Single(await echo.Outgoing.ReadAsync<Echoed>(
            cancellationToken: cancellationToken));
        Assert.Equal("committed", observed.Synapse.Value);
    }

    [Fact]
    public async Task RestartPreservesEvidenceAndExistingReferenceBecomesUsable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("restart");
        var reference = echo.Reference;

        await reference.Publish("before restart");
        var before = Assert.Single(await echo.Outgoing.ReadAsync<Echoed>(
            cancellationToken: cancellationToken));

        await echo.RestartHostAsync(cancellationToken);

        Assert.Same(reference, echo.Reference);
        Assert.Equal("after restart", await reference.Echo("after restart"));

        var preserved = Assert.Single(await echo.Outgoing.ReadAsync<Echoed>(
            cancellationToken: cancellationToken));
        Assert.Equal(before.Sequence, preserved.Sequence);
        Assert.Equal("before restart", preserved.Synapse.Value);
    }

    private const string ReminderName = "tests.clock";
}

#pragma warning disable CA1515 // Public probe interfaces model an external consumer assembly.
[ClientEntryPoint]
public partial interface IClockProbeNeuron : INeuron
{
    [Alias(nameof(ObserveTime))]
    Task ObserveTime();

    [Alias(nameof(ArmTimer))]
    Task ArmTimer(string value, TimeSpan dueTime);

    [Alias(nameof(ArmEqualTimers))]
    Task ArmEqualTimers(string first, string second, TimeSpan dueTime);

    [Alias(nameof(ArmSynchronouslyCompletedTimer))]
    Task ArmSynchronouslyCompletedTimer(string value, TimeSpan dueTime);

    [Alias(nameof(ArmPeriodicTimer))]
    Task ArmPeriodicTimer(
        string value,
        TimeSpan dueTime,
        TimeSpan period);

    [Alias(nameof(ChangeTimer))]
    Task ChangeTimer(
        string value,
        TimeSpan dueTime,
        TimeSpan period);

    [Alias(nameof(DisposeTimer))]
    Task DisposeTimer(string value);

    [Alias(nameof(DisposeTimerAwaited))]
    Task DisposeTimerAwaited(string value);

    [Alias(nameof(ArmReminder))]
    Task ArmReminder(TimeSpan dueTime);

    [Alias(nameof(ArmAndRefreshReminder))]
    Task ArmAndRefreshReminder();

    [Alias(nameof(UnregisterStaleReminder))]
    Task UnregisterStaleReminder();

    [Alias(nameof(ResetDiagnostics))]
    Task ResetDiagnostics();

    [Alias(nameof(ReadDiagnostics))]
    Task<string[]> ReadDiagnostics();
}
#pragma warning restore CA1515

[Alias("tests.clock-probe-timer-callback")]
[ClientEntryPoint]
internal partial interface IClockProbeTimerCallback : IGrainWithStringKey
{
    [Alias(nameof(TimerElapsed))]
    Task TimerElapsed(string value);
}

[GenerateSerializer]
[Alias("tests.time-observed")]
internal sealed record TimeObserved(
    [property: Id(0)] DateTimeOffset UtcNow) : Synapse;

[GenerateSerializer]
[Alias("tests.timer-fired")]
internal sealed record TimerFired(
    [property: Id(0)] string Value,
    [property: Id(1)] DateTimeOffset UtcNow) : Synapse;

[GenerateSerializer]
[Alias("tests.reminder-fired")]
internal sealed record ReminderFired(
    [property: Id(0)] DateTimeOffset UtcNow,
    [property: Id(1)] DateTime FirstTickTime,
    [property: Id(2)] TimeSpan Period,
    [property: Id(3)] DateTime CurrentTickTime) : Synapse;

internal sealed class ClockProbeNeuron :
    Neuron,
    IClockProbeNeuron,
    IClockProbeTimerCallback,
    IEmit<TimeObserved>,
    IEmit<TimerFired>,
    IEmit<ReminderFired>,
    IRemindable
{
    private const string ReminderName = "tests.clock";

    private readonly List<string> _diagnostics = [];
    private readonly Dictionary<string, ITimer> _timers = [];
    private IGrainReminder? _staleReminder;

    public Task ObserveTime()
        => EmitAsync(new TimeObserved(TimeProvider.GetUtcNow()));

    public Task ArmTimer(string value, TimeSpan dueTime)
    {
        var self = GrainFactory.GetGrain<IClockProbeTimerCallback>(
            this.GetGrainId());
        _timers.Add(value, TimeProvider.CreateTimer(
            _ => self.TimerElapsed(value).GetAwaiter().GetResult(),
            state: null,
            dueTime,
            Timeout.InfiniteTimeSpan));
        return Task.CompletedTask;
    }

    public Task ArmEqualTimers(
        string first,
        string second,
        TimeSpan dueTime)
    {
        _ = ArmTimer(first, dueTime);
        _ = ArmTimer(second, dueTime);
        return Task.CompletedTask;
    }

    public Task ArmSynchronouslyCompletedTimer(
        string value,
        TimeSpan dueTime)
    {
        var self = GrainFactory.GetGrain<IClockProbeTimerCallback>(
            this.GetGrainId());
        _timers.Add(value, TimeProvider.CreateTimer(
            _ => self.TimerElapsed(value).GetAwaiter().GetResult(),
            state: null,
            dueTime,
            Timeout.InfiniteTimeSpan));
        return Task.CompletedTask;
    }

    public Task ArmPeriodicTimer(
        string value,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var self = GrainFactory.GetGrain<IClockProbeTimerCallback>(
            this.GetGrainId());
        _timers.Add(value, TimeProvider.CreateTimer(
            _ => self.TimerElapsed(value).GetAwaiter().GetResult(),
            state: null,
            dueTime,
            period));
        return Task.CompletedTask;
    }

    public Task ChangeTimer(
        string value,
        TimeSpan dueTime,
        TimeSpan period)
    {
        AssertTimer(value).Change(dueTime, period);
        return Task.CompletedTask;
    }

    public Task DisposeTimer(string value)
    {
        AssertTimer(value).Dispose();
        return Task.CompletedTask;
    }

    public async Task DisposeTimerAwaited(string value)
        => await AssertTimer(value).DisposeAsync();

    public async Task ArmReminder(TimeSpan dueTime)
        => _ = await this.RegisterOrUpdateReminder(
            ReminderName,
            dueTime,
            TimeSpan.FromHours(1));

    public async Task ArmAndRefreshReminder()
    {
        _staleReminder = await this.RegisterOrUpdateReminder(
            ReminderName,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1));
        _ = await this.RegisterOrUpdateReminder(
            ReminderName,
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(1));
    }

    public Task UnregisterStaleReminder()
        => this.UnregisterReminder(
            _staleReminder
                ?? throw new InvalidOperationException(
                    "No stale reminder handle has been prepared."));

    public Task TimerElapsed(string value)
    {
        Record($"timer:{value}");
        return EmitAsync(new TimerFired(value, TimeProvider.GetUtcNow()));
    }

    public Task ResetDiagnostics()
    {
        _diagnostics.Clear();
        return Task.CompletedTask;
    }

    public Task<string[]> ReadDiagnostics()
        => Task.FromResult(_diagnostics.ToArray());

    async Task IRemindable.ReceiveReminder(
        string reminderName,
        TickStatus status)
    {
        if (!string.Equals(reminderName, ReminderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{nameof(ClockProbeNeuron)} does not own reminder '{reminderName}'.");
        }

        Record("reminder");
        await EmitAsync(new ReminderFired(
            TimeProvider.GetUtcNow(),
            status.FirstTickTime,
            status.Period,
            status.CurrentTickTime));
    }

    private void Record(string value)
        => _diagnostics.Add(value);

    private ITimer AssertTimer(string value)
        => _timers.TryGetValue(value, out var timer)
            ? timer
            : throw new InvalidOperationException(
                $"No timer named '{value}' is armed.");
}
