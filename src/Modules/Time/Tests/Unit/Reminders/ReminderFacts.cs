using DigitalBrain.Contracts;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using DigitalBrain.Time;
using DigitalBrain.Time.Reminders;
using DigitalBrain.Time.Reminders.Signals;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ReminderFacts
{
    [Fact]
    public async Task RegistrationUpdatesInPlaceValidatesBeforeMutationAndStopsIdempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ReminderControl();
        await using var brain = await StartAsync(ct, control);
        var reminder = brain.Get<IReminder>("registration");
        await reminder.Start(TimeSpan.FromDays(1), TimeSpan.FromMinutes(1));
        var initial = Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
        foreach (var due in new[] { TimeSpan.FromTicks(-1), Timeout.InfiniteTimeSpan, TimeSpan.MaxValue })
        { await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reminder.Start(due, TimeSpan.FromMinutes(1))); }
        foreach (var period in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(99), Timeout.InfiniteTimeSpan })
        { await Assert.ThrowsAnyAsync<ArgumentException>(() => reminder.Start(TimeSpan.Zero, period)); }
        Assert.Equal(initial.ETag, Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders).ETag);
        await reminder.Start(TimeSpan.FromDays(2), TimeSpan.FromMinutes(2));
        var updated = Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
        Assert.Equal(initial.ReminderName, updated.ReminderName);
        Assert.Equal(TimeSpan.FromMinutes(2), updated.Period);
        // Orleans 10.3.1 clamps future tick arithmetic and supports very long periods.
        await reminder.Start(TimeSpan.FromDays(1), TimeSpan.MaxValue);
        Assert.Equal(TimeSpan.MaxValue, Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders).Period);
        await reminder.Stop();
        await reminder.Stop();
        Assert.Empty((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
    }

    [Fact]
    public async Task ProviderFailuresReachCallerAndAllowRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ReminderControl { FailRegister = true };
        await using var brain = await StartAsync(ct, control);
        var reminder = brain.Get<IReminder>("failures");
        await Assert.ThrowsAsync<IOException>(() => reminder.Start(TimeSpan.FromDays(1), TimeSpan.FromMinutes(1)));
        Assert.Empty((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
        await reminder.Start(TimeSpan.FromDays(1), TimeSpan.FromMinutes(1));
        control.FailUnregister = true;
        var committed = Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders).ETag;
        control.FailRegister = true;
        await Assert.ThrowsAsync<IOException>(() => reminder.Start(TimeSpan.Zero, TimeSpan.FromMinutes(2)));
        Assert.Equal(committed, Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders).ETag);
        await Assert.ThrowsAsync<IOException>(reminder.Stop);
        Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
        await reminder.Stop();
        Assert.Empty((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
    }

    [Fact]
    public async Task LostAcknowledgementsAndFailedLookupCanBeRetriedWithoutDuplicateRegistrations()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ReminderControl { FailAfterRegister = true };
        await using var brain = await StartAsync(ct, control);
        var reminder = brain.Get<IReminder>("ambiguous");
        await Assert.ThrowsAsync<IOException>(() => reminder.Start(TimeSpan.FromDays(1), TimeSpan.FromMinutes(1)));
        Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
        await reminder.Start(TimeSpan.FromDays(1), TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders).Period);
        control.FailRead = true;
        await Assert.ThrowsAsync<IOException>(reminder.Stop);
        Assert.Single((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
        control.FailAfterUnregister = true;
        await Assert.ThrowsAsync<IOException>(reminder.Stop);
        Assert.Empty((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
        await reminder.Stop();
    }

    [Fact]
    public async Task NativeDefaultMinimumPeriodIsEnforced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var reminder = brain.Get<IReminder>("minimum");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => reminder.Start(TimeSpan.Zero, TimeSpan.FromSeconds(59)));
        await reminder.Start(TimeSpan.FromDays(1), TimeSpan.FromMinutes(1));
        await reminder.Stop();
    }

    [Fact]
    public async Task QueuedTicksAreAllowedAfterStopAndUnknownNamesAreIgnored()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ReminderControl();
        await using var brain = await StartAsync(ct, control);
        var reminder = brain.Get<IReminder>("queued");
        await using var ticks = await brain.Observe<ReminderTick>(reminder, ct);
        await reminder.Start(TimeSpan.FromDays(1), TimeSpan.FromMinutes(1));
        await reminder.Stop();
        // Inject already-queued deliveries through Orleans, without invoking a grain object directly.
        var receiver = reminder.AsReference<IRemindable>();
        await receiver.ReceiveReminder("unrelated", default);
        await receiver.ReceiveReminder("tick", default);
        Assert.Equal("queued", (await ticks.NextAsync(ct: ct)).ReminderId);
        await receiver.ReceiveReminder("tick", default);
        await ticks.NextAsync(ct: ct);
        Assert.Equal(2, ticks.Snapshot.Count);
        Assert.Empty((await control.Table.ReadRows(reminder.GetGrainId())).Reminders);
    }

    [Fact]
    public async Task ActualReminderPublishesTypedTicks()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ReminderControl();
        await using var brain = await StartAsync(ct, control);
        var reminder = brain.Get<IReminder>("live");
        await using var ticks = await brain.Observe<ReminderTick>(reminder, ct);
        await reminder.Start(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        Assert.Equal("live", (await ticks.NextAsync(ct: ct)).ReminderId);
        Assert.Equal("live", (await ticks.NextAsync(ct: ct)).ReminderId);
        await reminder.Stop();
    }

    [Fact]
    public async Task ReminderReactivatesAfterExplicitDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var control = new ReminderControl();
        await using var brain = await StartAsync(ct, control);
        var reminder = brain.Get<IReminder>("reactivate");
        await reminder.Start(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        var before = await control.NextDeliveryAsync(ct);
        await brain.DeactivateAsync(reminder, ct);
        ReminderDelivery after;
        do { after = await control.NextDeliveryAsync(ct); }
        while (ReferenceEquals(before.Activation, after.Activation));
        Assert.Equal(before.Id, after.Id);
        await reminder.Stop();
    }

    [Fact]
    public async Task MissingReminderConfigurationFailsStartup()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var brain = await UnitTest.Create().WithModule<TimeModule>()
            .StartAsync(TestContext.Current.CancellationToken);
        });
        Assert.Contains("reminder", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task<UnitBrain> StartAsync(CancellationToken ct, ReminderControl? reminders = null)
        => UnitTest.Create().WithModule<TimeModule>().WithReminders()
            .ConfigureSilo(silo => { if (reminders is not null) { silo.UseReminderControl(reminders); } })
            .StartAsync(ct);
}