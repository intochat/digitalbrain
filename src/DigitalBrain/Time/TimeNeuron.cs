namespace DigitalBrain.Time;

/// <summary>
/// The time neuron of a brain (<c>{brain}/time/default</c>). It runs one reminder per distinct schedule text, however many owners
/// share it, and publishes one durable <see cref="Tick"/> per firing.
/// </summary>
/// <remarks>
/// Daily and weekly schedules re-anchor their dueTime to the next true UTC boundary on every tick,
/// eliminating reminder drift and keeping ticks aligned with calendar time.
/// </remarks>
[Neuron("time")]
public sealed class TimeNeuron : Neuron<TimeState>, ITime
{
    /// <inheritdoc />
    public async Task Register(string owner, string schedule, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        Schedule parsed;
        try { parsed = Schedule.Parse(schedule); }
        catch (FormatException error) { throw new ArgumentException(error.Message, nameof(schedule), error); }

        if (State.Registrations.Any(r => r.Owner == owner && r.Schedule == schedule)) { return; }
        await Save(new TimeState([.. State.Registrations, new ScheduleRegistration(owner, schedule)]), [new Registered(owner, schedule)]).ConfigureAwait(true);
        await Start(parsed).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task Unregister(string owner, CancellationToken ct)
    {
        var removed = State.Registrations.Where(r => r.Owner == owner).ToList();
        if (removed.Count == 0) { return; }
        var kept = State.Registrations.Where(r => r.Owner != owner).ToList();
        await Save(new TimeState(kept), [.. removed.Select(r => new Unregistered(r.Owner, r.Schedule))]).ConfigureAwait(true);
        foreach (var text in removed.Select(r => r.Schedule).Distinct().Where(text => kept.All(r => r.Schedule != text)))
        {
            await Context.Reminders.Stop(text).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRegistration>> Registered(CancellationToken ct) => Task.FromResult(State.Registrations);

    /// <inheritdoc />
    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        foreach (var text in State.Registrations.Select(r => r.Schedule).Distinct())
        {
            await Start(Schedule.Parse(text)).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        if (State.Registrations.Any(r => r.Schedule == name))
        {
            var now = Context.Time.GetUtcNow();
            await Publish(new Tick(name, now)).ConfigureAwait(true);
            try
            {
                var schedule = Schedule.Parse(name);
                var dueTime = schedule.NextAfter(now) - now;
                await Context.Reminders.Start(schedule.Text, dueTime, schedule.Period).ConfigureAwait(true);
            }
            catch (FormatException)
            {
            }
        }
    }

    // Starting a reminder again replaces it, so registering and activating are idempotent.
    private Task Start(Schedule schedule)
    {
        var now = Context.Time.GetUtcNow();
        return Context.Reminders.Start(schedule.Text, schedule.NextAfter(now) - now, schedule.Period);
    }
}
