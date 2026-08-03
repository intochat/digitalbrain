using DigitalBrain.Abstractions;
using DigitalBrain.Testing;

namespace DigitalBrain.Time.Tests;

public abstract class CountdownTest : NeuronTest<ICountdown>
{
    protected const string Countdown = "countdown";
    protected const string Destination = "destination";

    protected const string OccurrenceCommitFailure = "countdown occurrence commit failure";
    protected const string StartStateCommitFailure = "start state commit failure";
    protected const string RescheduleStateCommitFailure = "reschedule state commit failure";
    protected const string RestartStateCommitFailure = "restart state commit failure";

    protected override void Compose(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<TimeModule>();
    }

    protected async ValueTask<(TestNeuron<ICountdown> Countdown, TestNeuron<ICountdown> Destination)>
        PairAsync()
        => (await NeuronAsync(Countdown), await NeuronAsync(Destination));

    protected static Task<CountdownSnapshot> StartAsync(
        TestNeuron<ICountdown> countdown,
        TestNeuron<ICountdown> destination,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(countdown);
        ArgumentNullException.ThrowIfNull(destination);
        return countdown.Reference.Start(new StartCountdown(CommandId.New(), duration, destination.Id));
    }

    protected async ValueTask<(
        TestNeuron<ICountdown> Countdown,
        TestNeuron<ICountdown> Destination,
        CountdownSnapshot Started)> ScheduleAsync(TimeSpan duration)
    {
        var (countdown, destination) = await PairAsync();
        var started = await StartAsync(countdown, destination, duration);
        return (countdown, destination, started);
    }
}
