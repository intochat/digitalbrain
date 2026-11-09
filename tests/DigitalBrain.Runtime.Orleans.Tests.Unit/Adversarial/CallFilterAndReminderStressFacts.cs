using System.Diagnostics;
using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Identity;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Time;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Adversarial;

/// <summary>
/// Empirical adversarial verification of:
/// 1. DigitalBrainCallFilter context propagation, traceparent propagation, and tenant isolation.
/// 2. TimeNeuron UTC anchored schedules, jitter invariance, and boundary roll-overs.
/// </summary>
public sealed class CallFilterAndReminderStressFacts
{
    // =========================================================================
    // Area 1: DigitalBrainCallFilter Adversarial & Edge Cases
    // =========================================================================

    [Theory]
    [InlineData(CallerKind.User)]
    [InlineData(CallerKind.App)]
    [InlineData(CallerKind.Agent)]
    [InlineData(CallerKind.Scheduler)]
    public async Task CallFilter_CrossBrainNeuronInvocation_StrictlyBlockedForNonPlatform(CallerKind kind)
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Of("attacker-principal", "acct-1", "brain-attacker", kind, appId: kind == CallerKind.App ? "malicious-app" : null);
        using var scope = RuntimeCallerContext.StampScope(caller);

        var context = new MockIncomingContext(GrainId.Create("neuron", "brain-victim/apps/vault"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
        Assert.Contains("Cross-brain security violation", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brain-attacker", ex.Message, StringComparison.Ordinal);
        Assert.Contains("brain-victim", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallFilter_PlatformCaller_PermittedAcrossAnyBrain()
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Platform("kernel-principal", "kernel-acct", "kernel-brain");
        using var scope = RuntimeCallerContext.StampScope(caller);

        var executed = false;
        var context = new MockIncomingContext(
            GrainId.Create("neuron", "tenant-customer-42/apps/settings"),
            () => { executed = true; return Task.CompletedTask; });

        await filter.Invoke(context);
        Assert.True(executed, "Platform caller must be permitted cross-brain.");
    }

    [Fact]
    public async Task CallFilter_SystemBrainNeuron_PermittedFromAnyTenantBrain()
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Of("user-1", "acct-1", "tenant-alpha", CallerKind.User);
        using var scope = RuntimeCallerContext.StampScope(caller);

        var executed = false;
        // System scopes in DigitalBrain start with '@' (e.g. '@kernel', '@system')
        var context = new MockIncomingContext(
            GrainId.Create("neuron", "@system/templates/starter"),
            () => { executed = true; return Task.CompletedTask; });

        await filter.Invoke(context);
        Assert.True(executed, "System brain (@system) neurons must be globally readable by any tenant.");
    }

    [Fact]
    public async Task CallFilter_UnadornedSystemTenant_BlockedWithoutLeadingAtSymbol()
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Of("user-1", "acct-1", "tenant-alpha", CallerKind.User);
        using var scope = RuntimeCallerContext.StampScope(caller);

        // An unadorned 'system' without '@' is treated as an ordinary tenant brain id, NOT a system scope!
        var context = new MockIncomingContext(GrainId.Create("neuron", "system/templates/starter"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filter.Invoke(context));
        Assert.Contains("Cross-brain security violation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallFilter_NonNeuronGrain_CrossBrainCheckDoesNotBlock()
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Of("user-1", "acct-1", "tenant-alpha", CallerKind.User);
        using var scope = RuntimeCallerContext.StampScope(caller);

        var executed = false;
        // Target is a stateless worker or non-neuron grain (e.g. "ino.data-analytics-worker")
        var context = new MockIncomingContext(
            GrainId.Create("ino.data-analytics-worker", "0"),
            () => { executed = true; return Task.CompletedTask; });

        await filter.Invoke(context);
        Assert.True(executed, "Non-neuron grains (like stateless workers) must not be rejected by brain isolation.");
    }

    [Fact]
    public async Task CallFilter_MalformedTargetKey_DoesNotCrashFilter()
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Of("user-1", "acct-1", "tenant-alpha", CallerKind.User);
        using var scope = RuntimeCallerContext.StampScope(caller);

        var executed = false;
        // Target key without slashes (not a valid compound key)
        var context = new MockIncomingContext(
            GrainId.Create("neuron", "invalid-key-no-slashes"),
            () => { executed = true; return Task.CompletedTask; });

        await filter.Invoke(context);
        Assert.True(executed, "Malformed compound grain keys should be handled gracefully without crashing the filter.");
    }

    [Fact]
    public async Task CallFilter_Outgoing_PropagatesFullCallerAndPreservesExistingCallerProvenance()
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Of("principal-99", "acct-88", "brain-77", CallerKind.App, appId: "app-66", conversationId: "conv-55");
        using var scope = RuntimeCallerContext.StampScope(caller);

        // Pre-set existing db.caller provenance
        RequestContext.Set(HostNeuronContext.CallerKey, "preexisting-caller-neuron");

        var executed = false;
        var outgoingContext = new MockOutgoingContext(
            sourceGrainId: GrainId.Create("neuron", "brain-77/apps/app-66"),
            targetGrainId: GrainId.Create("neuron", "brain-77/edge/storage"),
            onInvoke: () =>
            {
                executed = true;
                Assert.Equal("principal-99", RequestContext.Get("db.cap.principal"));
                Assert.Equal("acct-88", RequestContext.Get("db.cap.account"));
                Assert.Equal("brain-77", RequestContext.Get("db.cap.brain"));
                Assert.Equal("app-66", RequestContext.Get("db.cap.app"));
                Assert.Equal("conv-55", RequestContext.Get("db.cap.conversation"));
                // Existing caller key must be preserved
                Assert.Equal("preexisting-caller-neuron", RequestContext.Get(HostNeuronContext.CallerKey));
                return Task.CompletedTask;
            });

        await filter.Invoke(outgoingContext);
        Assert.True(executed);
    }

    [Fact]
    public async Task CallFilter_Outgoing_PopulatesSourceGrainAsCallerWhenNonePresent()
    {
        var filter = new DigitalBrainCallFilter();
        var caller = CapabilityCallers.Of("user-1", "acct-1", "brain-1", CallerKind.User);
        using var scope = RuntimeCallerContext.StampScope(caller);

        RequestContext.Remove(HostNeuronContext.CallerKey);

        var executed = false;
        var outgoingContext = new MockOutgoingContext(
            sourceGrainId: GrainId.Create("neuron", "brain-1/apps/calc"),
            targetGrainId: GrainId.Create("neuron", "brain-1/storage/default"),
            onInvoke: () =>
            {
                executed = true;
                Assert.Equal("brain-1/apps/calc", RequestContext.Get(HostNeuronContext.CallerKey));
                return Task.CompletedTask;
            });

        await filter.Invoke(outgoingContext);
        Assert.True(executed);
    }

    [Fact]
    public async Task CallFilter_TraceParentPropagation_IncomingStartsActivityWithCorrectParent()
    {
        var filter = new DigitalBrainCallFilter();
        const string incomingTrace = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        RequestContext.Set("traceparent", incomingTrace);

        Activity? capturedActivity = null;
        var context = new MockIncomingContext(
            GrainId.Create("neuron", "brain-alpha/time/default"),
            () =>
            {
                capturedActivity = Activity.Current;
                return Task.CompletedTask;
            });

        await filter.Invoke(context);

        Assert.NotNull(capturedActivity);
        Assert.Equal(incomingTrace, capturedActivity.ParentId);
        capturedActivity.Stop();
        RequestContext.Remove("traceparent");
    }

    // =========================================================================
    // Area 3: TimeNeuron UTC Anchored Reminders & Jitter Invariance
    // =========================================================================

    [Fact]
    public void Schedule_Daily_AlignsStrictlyToUtcBoundaryAcrossAllMinutesOfDay()
    {
        var schedule = Schedule.Parse("14:00");
        Assert.Equal(ScheduleKind.Daily, schedule.Kind);
        Assert.Equal(TimeSpan.FromDays(1), schedule.Period);

        // Test every hour of 2026-11-09 in UTC
        var baseDate = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

        for (var hour = 0; hour < 24; hour++)
        {
            var now = baseDate.AddHours(hour).AddMinutes(30);
            var next = schedule.NextAfter(now);

            // Must always be at 14:00:00 UTC
            Assert.Equal(14, next.Hour);
            Assert.Equal(0, next.Minute);
            Assert.Equal(0, next.Second);
            Assert.Equal(TimeSpan.Zero, next.Offset);

            if (hour < 14)
            {
                // Must be today at 14:00
                Assert.Equal(baseDate.Date, next.Date);
            }
            else
            {
                // Must be tomorrow at 14:00
                Assert.Equal(baseDate.AddDays(1).Date, next.Date);
            }

            // Next must strictly be in the future
            Assert.True(next > now);
        }
    }

    [Fact]
    public void Schedule_Weekly_AlignsStrictlyToTargetDayAndUtcTime()
    {
        var schedule = Schedule.Parse("Mon 09:00");
        Assert.Equal(ScheduleKind.Weekly, schedule.Kind);
        Assert.Equal(DayOfWeek.Monday, schedule.Day);
        Assert.Equal(new TimeOnly(9, 0), schedule.At);

        // Starting from Sunday 2026-11-08 through Saturday 2026-11-14
        var sunday = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);

        for (var dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            var now = sunday.AddDays(dayOffset);
            var next = schedule.NextAfter(now);

            Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
            Assert.Equal(9, next.Hour);
            Assert.Equal(0, next.Minute);
            Assert.Equal(0, next.Second);
            Assert.Equal(TimeSpan.Zero, next.Offset);
            Assert.True(next > now);
        }
    }

    [Fact]
    public void Schedule_NextAfter_NoCumulativeJitterAcross1000SimulatedTicks()
    {
        var schedule = Schedule.Parse("18:00");
        var currentClock = new DateTimeOffset(2026, 1, 1, 18, 0, 0, TimeSpan.Zero);
        var rng = new Random(42);

        // Simulate 1,000 daily reminder firings with random late arrival (0 to 45 seconds jitter)
        for (var tick = 0; tick < 1000; tick++)
        {
            var jitterSeconds = rng.Next(0, 45);
            var jitterMilliseconds = rng.Next(0, 999);
            var actualFireTime = currentClock.AddSeconds(jitterSeconds).AddMilliseconds(jitterMilliseconds);

            // Calculation as executed in TimeNeuron.OnReminder:
            // var dueTime = schedule.NextAfter(now) - now;
            var nextUtc = schedule.NextAfter(actualFireTime);
            var dueTime = nextUtc - actualFireTime;

            // Target time when Orleans will wake up next:
            var scheduledWakeUp = actualFireTime + dueTime;

            // The scheduled wakeup MUST hit the exact next calendar day at 18:00:00.000 UTC!
            var expectedNextUtc = currentClock.AddDays(1);
            Assert.Equal(expectedNextUtc, scheduledWakeUp);
            Assert.Equal(18, scheduledWakeUp.Hour);
            Assert.Equal(0, scheduledWakeUp.Minute);
            Assert.Equal(0, scheduledWakeUp.Second);
            Assert.Equal(0, scheduledWakeUp.Millisecond);

            // Advance clock to next scheduled occurrence for the next iteration
            currentClock = expectedNextUtc;
        }
    }

    [Fact]
    public void Schedule_BoundaryTransitions_HandlesMonthYearAndLeapDayAccurately()
    {
        var schedule = Schedule.Parse("00:00");

        // 1. Year boundary: 2026-12-31 23:59:30 UTC -> 2027-01-01 00:00:00 UTC
        var newYearsEve = new DateTimeOffset(2026, 12, 31, 23, 59, 30, TimeSpan.Zero);
        var nextYear = schedule.NextAfter(newYearsEve);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), nextYear);

        // 2. Leap year boundary: 2028-02-28 23:59:45 UTC -> 2028-02-29 00:00:00 UTC
        var leapEve = new DateTimeOffset(2028, 2, 28, 23, 59, 45, TimeSpan.Zero);
        var leapDay = schedule.NextAfter(leapEve);
        Assert.Equal(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero), leapDay);

        // 3. Leap day to March 1: 2028-02-29 23:59:50 UTC -> 2028-03-01 00:00:00 UTC
        var leapEnd = new DateTimeOffset(2028, 2, 29, 23, 59, 50, TimeSpan.Zero);
        var marchFirst = schedule.NextAfter(leapEnd);
        Assert.Equal(new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), marchFirst);
    }

    // =========================================================================
    // Mock Contexts
    // =========================================================================

    private sealed class MockIncomingContext(GrainId targetGrainId, Func<Task>? onInvoke = null) : IIncomingGrainCallContext
    {
        public GrainId TargetId => targetGrainId;
        public GrainId? SourceId => null;
        public Task Invoke() => onInvoke is not null ? onInvoke() : Task.CompletedTask;

        IGrainContext IIncomingGrainCallContext.TargetContext => null!;
        IInvokable IGrainCallContext.Request => null!;
        Response? IGrainCallContext.Response { get; set; }
        object? IGrainCallContext.Result { get; set; }
        object IGrainCallContext.Grain => null!;
        MethodInfo IGrainCallContext.InterfaceMethod => null!;
        MethodInfo IIncomingGrainCallContext.ImplementationMethod => null!;
        GrainInterfaceType IGrainCallContext.InterfaceType => default;
        string IGrainCallContext.InterfaceName => string.Empty;
        string IGrainCallContext.MethodName => string.Empty;
    }

    private sealed class MockOutgoingContext(GrainId? sourceGrainId, GrainId targetGrainId, Func<Task>? onInvoke = null) : IOutgoingGrainCallContext
    {
        public GrainId? SourceId => sourceGrainId;
        public GrainId TargetId => targetGrainId;
        public Task Invoke() => onInvoke is not null ? onInvoke() : Task.CompletedTask;

        IGrainContext IOutgoingGrainCallContext.SourceContext => null!;
        IInvokable IGrainCallContext.Request => null!;
        Response? IGrainCallContext.Response { get; set; }
        object? IGrainCallContext.Result { get; set; }
        object IGrainCallContext.Grain => null!;
        MethodInfo IGrainCallContext.InterfaceMethod => null!;
        GrainInterfaceType IGrainCallContext.InterfaceType => default;
        string IGrainCallContext.InterfaceName => string.Empty;
        string IGrainCallContext.MethodName => string.Empty;
    }
}
