using DigitalBrain.Kernel;
using DigitalBrain.Time;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class ScheduleDiffFacts
{
    private sealed class FakeTime : ITime
    {
        public List<ScheduleRegistration> Registrations { get; } = [];
        public List<string> UnregisteredOwners { get; } = [];
        public List<(string Owner, string Schedule)> RegisteredCalls { get; } = [];

        public Task<IReadOnlyList<ScheduleRegistration>> Registered(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScheduleRegistration>>(Registrations.ToList());

        public Task Register(string owner, string schedule, CancellationToken cancellationToken = default)
        {
            RegisteredCalls.Add((owner, schedule));
            Registrations.Add(new ScheduleRegistration(owner, schedule));
            return Task.CompletedTask;
        }

        public Task Unregister(string owner, CancellationToken cancellationToken = default)
        {
            UnregisteredOwners.Add(owner);
            Registrations.RemoveAll(r => r.Owner == owner);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Diff_detects_unchanged_added_and_removed_schedules()
    {
        var held = new[] { "cron: 0 0 * * *", "every: 1h" };
        var wanted = new[] { "every: 1h", "cron: 0 12 * * *" };

        var delta = ScheduleDiff.Diff(held, wanted);

        Assert.True(delta.HasChanges);
        Assert.Single(delta.Unchanged);
        Assert.Contains("every: 1h", delta.Unchanged);

        Assert.Single(delta.Added);
        Assert.Contains("cron: 0 12 * * *", delta.Added);

        Assert.Single(delta.Removed);
        Assert.Contains("cron: 0 0 * * *", delta.Removed);
    }

    [Fact]
    public void Diff_returns_no_changes_for_equivalent_schedules_in_different_order()
    {
        var held = new[] { "a", "b", "c" };
        var wanted = new[] { "c", "a", "b" };

        var delta = ScheduleDiff.Diff(held, wanted);

        Assert.False(delta.HasChanges);
        Assert.Equal(3, delta.Unchanged.Count);
        Assert.Empty(delta.Added);
        Assert.Empty(delta.Removed);
        Assert.False(ScheduleDiff.NeedsUpdate(held, wanted));
    }

    [Fact]
    public async Task Apply_leaves_reminders_intact_when_schedules_are_unchanged()
    {
        var time = new FakeTime();
        time.Registrations.Add(new ScheduleRegistration("neuron-1", "every 5m"));

        var schedules = new List<Schedule> { Schedule.Parse("every 5m") };

        await ScheduleDiff.Apply(time, "neuron-1", schedules, CancellationToken.None);

        Assert.Empty(time.UnregisteredOwners);
        Assert.Empty(time.RegisteredCalls);
    }

    [Fact]
    public async Task Apply_updates_schedules_only_when_definitions_differ()
    {
        var time = new FakeTime();
        time.Registrations.Add(new ScheduleRegistration("neuron-1", "every 5m"));

        var schedules = new List<Schedule> { Schedule.Parse("every 10m") };

        await ScheduleDiff.Apply(time, "neuron-1", schedules, CancellationToken.None);

        Assert.Single(time.UnregisteredOwners);
        Assert.Equal("neuron-1", time.UnregisteredOwners[0]);

        Assert.Single(time.RegisteredCalls);
        Assert.Equal(("neuron-1", "every 10m"), time.RegisteredCalls[0]);
    }
}
