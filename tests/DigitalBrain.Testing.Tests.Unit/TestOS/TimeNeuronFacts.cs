using DigitalBrain.Time;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Time;

public sealed class TimeNeuronFacts
{
    private static readonly NeuronId TimeId = NeuronId.Parse("b/time/default");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OS Start()
    {
        var os = OS.Local(_ => { }).Register(typeof(TimeNeuronFacts).Assembly);
        os.Clock.Advance(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero) - os.Clock.GetUtcNow());
        return os;
    }

    private static List<Tick> Ticks(OS os) => [.. os.Journal(TimeId.Brain).Signals(TimeId).Select(item => item.Signal).OfType<Tick>()];

    [Fact]
    public async Task RegistrationIsListedAndRepeatingItChangesNothing()
    {
        await using var os = Start();
        var time = os.Get<ITime>(TimeId);
        await time.Register("app", "07:00", Ct);
        await time.Register("app", "07:00", Ct);

        Assert.Equal([new ScheduleRegistration("app", "07:00")], await time.Registered(Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => time.Register("app", "nonsense", Ct));
    }

    [Fact]
    public async Task ATickIsJournaledWhenTheReminderFires()
    {
        await using var os = Start();
        await os.Get<ITime>(TimeId).Register("app", "07:00", Ct);

        os.Clock.Advance(TimeSpan.FromMinutes(59));
        await os.Idle();
        Assert.Empty(Ticks(os));

        os.Clock.Advance(TimeSpan.FromMinutes(1));
        await os.Idle();
        var tick = Assert.Single(Ticks(os));
        Assert.Equal("07:00", tick.Schedule);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.Zero), tick.At);
    }

    [Fact]
    public async Task UnregisterStopsTheReminderSoALaterAdvancePublishesNothing()
    {
        await using var os = Start();
        var time = os.Get<ITime>(TimeId);
        await time.Register("app", "07:00", Ct);
        await time.Unregister("app", Ct);

        os.Clock.Advance(TimeSpan.FromHours(2));
        await os.Idle();

        Assert.Empty(Ticks(os));
        Assert.Empty(await time.Registered(Ct));
    }

    [Fact]
    public async Task TwoOwnersSharingAScheduleGetOneTickPerFiringAndTheLastUnregisterStopsIt()
    {
        await using var os = Start();
        var time = os.Get<ITime>(TimeId);
        await time.Register("a", "every 30m", Ct);
        await time.Register("b", "every 30m", Ct);

        os.Clock.Advance(TimeSpan.FromMinutes(30));
        await os.Idle();
        Assert.Single(Ticks(os));

        await time.Unregister("a", Ct);
        os.Clock.Advance(TimeSpan.FromMinutes(30));
        await os.Idle();
        Assert.Equal(2, Ticks(os).Count);

        await time.Unregister("b", Ct);
        os.Clock.Advance(TimeSpan.FromMinutes(30));
        await os.Idle();
        Assert.Equal(2, Ticks(os).Count);
    }
}
