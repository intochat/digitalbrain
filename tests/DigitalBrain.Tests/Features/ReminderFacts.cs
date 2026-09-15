using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Xunit;
using ITimer = DigitalBrain.Time.ITimer;

namespace DigitalBrain.Tests;

public sealed class ReminderFacts
{
    [Fact]
    public async Task Concurrent_reminders_have_independent_alarms_and_cancellation()
    {
        await using var brain = await Start();
        var reminders = Reminders(brain);
        var due = DateTimeOffset.UtcNow.AddSeconds(4).ToUnixTimeSeconds();
        await Task.WhenAll(reminders.Schedule(new(CommandId.New(), "cancel", "Cancelled reminder", due)),
            reminders.Schedule(new(CommandId.New(), "deliver", "Call Alice", due)));
        await ReactionWait.UntilAsync(async () => (await reminders.Read()).Items.Count == 2, TestContext.Current.CancellationToken);
        await reminders.Cancel(new(CommandId.New(), "cancel"));
        var output = await ReactionWait.ForSignalAsync(reminders, TimeSignals.ReminderDue, TestContext.Current.CancellationToken);
        Assert.Equal(new ReminderDue("deliver", "Call Alice", due), output.Body(TimeJson.Default.ReminderDue));
        var state = await reminders.Read();
        Assert.Equal(ReminderStatus.Cancelled, state.Items.Single(item => item.ReminderId == "cancel").Status);
        Assert.Equal(ReminderStatus.Delivered, state.Items.Single(item => item.ReminderId == "deliver").Status);
        Assert.Single((await reminders.ReadJournal(JournalKind.Outgoing, 0)).Delta, delivery => delivery.Signal.Type == TimeSignals.ReminderDue);
    }

    [Fact]
    public async Task Duplicate_commands_and_events_do_not_reschedule_or_overwrite()
    {
        await using var brain = await Start();
        var reminders = Reminders(brain);
        var command = new SetReminder(CommandId.New(), "once", "One alarm", DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeSeconds());
        var accepted = await reminders.Schedule(command);
        Assert.Equal(accepted, await reminders.Schedule(command));
        await ReactionWait.UntilAsync(async () => (await reminders.Read()).Items.Count == 1, TestContext.Current.CancellationToken);
        await reminders.Schedule(command with { Id = CommandId.New(), Text = "Changed" });
        var refusal = await ReactionWait.ForSignalAsync(reminders, TimeSignals.ReminderRejected, TestContext.Current.CancellationToken);
        Assert.Equal("once", refusal.Body(TimeJson.Default.ReminderRejected)!.ReminderId);
        await reminders.Schedule(command with { Id = CommandId.New() });
        await ReactionWait.ForSignalAsync(reminders, TimeSignals.ReminderDue, TestContext.Current.CancellationToken);
        await reminders.Schedule(command with { Id = CommandId.New() });
        await ReactionWait.UntilAsync(async () => await reminders.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        Assert.Single((await reminders.Read()).Items);
        Assert.Single((await reminders.ReadJournal(JournalKind.Outgoing, 0)).Delta, delivery => delivery.Signal.Type == TimeSignals.ReminderDue);
    }

    [Fact]
    public async Task Cold_restart_preserves_due_time_and_delivers_only_once()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-reminders", Guid.NewGuid().ToString("N"));
        var command = new SetReminder(CommandId.New(), "restart", "Recovered reminder", DateTimeOffset.UtcNow.AddSeconds(8).ToUnixTimeSeconds());
        await using (var brain = await Start(directory))
        {
            // Calculate after startup so the ingress deadline remains in the future on slow machines.
            command = command with { DueUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(3).ToUnixTimeSeconds() };
            var reminders = Reminders(brain);
            await reminders.Schedule(command);
            await ReactionWait.UntilAsync(async () => (await reminders.Read()).Items.Count == 1 && await reminders.ReadPendingCount() == 0,
                TestContext.Current.CancellationToken);
        }
        await using (var brain = await Start(directory))
        {
            var reminders = Reminders(brain);
            Assert.Equal(command.DueUnixSeconds, Assert.Single((await reminders.Read()).Items).DueUnixSeconds);
            await reminders.Schedule(command with { Id = CommandId.New() });
            var signal = await ReactionWait.ForSignalAsync(reminders, TimeSignals.ReminderDue, TestContext.Current.CancellationToken);
            Assert.Equal(command.DueUnixSeconds, signal.Body(TimeJson.Default.ReminderDue)!.DueUnixSeconds);
            await ReactionWait.UntilAsync(async () => await reminders.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
            Assert.Single((await reminders.ReadJournal(JournalKind.Outgoing, 0)).Delta, delivery => delivery.Signal.Type == TimeSignals.ReminderDue);
        }
    }

    [Fact]
    public async Task Absolute_timer_recovers_an_overdue_deadline_without_rebasing()
    {
        await using var brain = await Start();
        var timer = brain.Grains.GetGrain<ITimer>(new NeuronId("timer", "overdue").ToGrainId());
        var due = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var command = new ScheduleTimerAt(CommandId.New(), due, "Overdue");
        var accepted = await timer.ScheduleAt(command);
        Assert.Equal(accepted, await timer.ScheduleAt(command));
        var signal = await ReactionWait.ForSignalAsync(timer, TimeSignals.TimerElapsed, TestContext.Current.CancellationToken);
        var elapsed = signal.Body(TimeJson.Default.TimerElapsedBody)!;
        Assert.Equal(due, elapsed.DueAt.ToUnixTimeSeconds());
        Assert.Equal(TimerResolution.Recovered, elapsed.Resolution);
        Assert.Equal(1, (await timer.Read()).Generation);
    }

    [Fact]
    public async Task Business_refusals_announce_rejections_and_allow_later_valid_work()
    {
        await using var brain = await Start();
        var reminders = Reminders(brain);
        await reminders.Schedule(new(CommandId.New(), "past", "Past", 1));
        await reminders.Schedule(new(CommandId.New(), "overflow", "Future", long.MaxValue));
        await reminders.Schedule(new(CommandId.New(), "blank", " ", DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds()));
        await ReactionWait.UntilAsync(async () =>
            (await reminders.ReadJournal(JournalKind.Outgoing, 0)).Delta.Count(delivery => delivery.Signal.Type == TimeSignals.ReminderRejected) == 3,
            TestContext.Current.CancellationToken);
        Assert.Empty((await reminders.Read()).Items);
        Assert.DoesNotContain((await reminders.ReadJournal(JournalKind.Outgoing, 0)).Delta, delivery => delivery.Signal.Type == TimeSignals.ReminderDue);
        await reminders.Schedule(new(CommandId.New(), "valid", "Still works", DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeSeconds()));
        var due = await ReactionWait.ForSignalAsync(reminders, TimeSignals.ReminderDue, TestContext.Current.CancellationToken);
        Assert.Equal("valid", due.Body(TimeJson.Default.ReminderDue)!.ReminderId);
        Assert.Equal("valid", Assert.Single((await reminders.Read()).Items).ReminderId);
    }

    [Fact]
    public async Task Malformed_identity_is_rejected_before_acceptance()
    {
        await using var brain = await Start();
        var reminders = Reminders(brain);
        await Assert.ThrowsAsync<CommandRejectedException>(() => reminders.Schedule(new(CommandId.New(), "", "Invalid ID", 1)));
        Assert.Equal(0, await reminders.ReadPendingCount());
    }

    [Fact]
    public async Task Recovered_request_validates_its_original_admission_time()
    {
        await using var brain = await Start();
        var reminders = Reminders(brain);
        var due = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        // Reproduce a durable admission replayed after its original due time.
        var body = new ReminderScheduling("delayed", "Accepted before outage", due, due - 60);
        await reminders.Deliver(SignalDelivery.Create(
            Signal.FromJson("ReminderScheduling", body, TimeJson.Default.ReminderScheduling),
            new NeuronId("reminders", "telegram-42"), 1, TimeProvider.System), TestContext.Current.CancellationToken);
        var signal = await ReactionWait.ForSignalAsync(reminders, TimeSignals.ReminderDue, TestContext.Current.CancellationToken);
        Assert.Equal(due, signal.Body(TimeJson.Default.ReminderDue)!.DueUnixSeconds);
        Assert.Equal(ReminderStatus.Delivered, Assert.Single((await reminders.Read()).Items).Status);
        Assert.DoesNotContain((await reminders.ReadJournal(JournalKind.Outgoing, 0)).Delta, delivery => delivery.Signal.Type == TimeSignals.ReminderRejected);
    }

    private static IReminders Reminders(BrainSimulation brain) => brain.Grains.GetGrain<IReminders>(new NeuronId("reminders", "telegram-42").ToGrainId());

    private static Task<BrainSimulation> Start(string? directory = null) => BrainSimulation.StartAsync(new()
    {
        Modules = new([typeof(TimeModule)]),
        PersistenceDirectory = directory,
        ConfigureSilo = silo =>
        {
            silo.Services.AddSingleton(new TimeOptions { AlarmPeriod = TimeSpan.FromSeconds(1) });
            silo.Services.Configure<ReminderOptions>(options => options.MinimumReminderPeriod = TimeSpan.FromSeconds(1));
        }
    });
}
