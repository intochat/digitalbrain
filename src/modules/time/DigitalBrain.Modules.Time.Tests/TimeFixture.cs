using DigitalBrain.Abstractions;
using DigitalBrain.Testing;

namespace DigitalBrain.Time.Tests;

public sealed class TimeFixture : DigitalBrainFixture
{
    public const string Countdown = "countdown";
    public const string Destination = "destination";

    public const string OccurrenceCommitFailure = "countdown occurrence commit failure";
    public const string StartStateCommitFailure = "start state commit failure";
    public const string RescheduleStateCommitFailure = "reschedule state commit failure";
    public const string RestartStateCommitFailure = "restart state commit failure";

    public static (TestNeuron<ICountdown> Countdown, TestNeuron<ICountdown> Destination)
        Pair(TestBrain test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return (test.Neuron<ICountdown>(Countdown), test.Neuron<ICountdown>(Destination));
    }

    public static Task<CountdownSnapshot> Start(
        TestNeuron<ICountdown> countdown,
        TestNeuron<ICountdown> destination,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(countdown);
        ArgumentNullException.ThrowIfNull(destination);
        return countdown.Reference.Start(new StartCountdown(
            CommandId.New(),
            duration,
            destination.Id));
    }

    public static async Task<(
        TestNeuron<ICountdown> Countdown,
        TestNeuron<ICountdown> Destination,
        CountdownSnapshot Started)> Schedule(
        TestBrain test,
        TimeSpan duration)
    {
        var (countdown, destination) = Pair(test);
        var started = await Start(countdown, destination, duration);
        return (countdown, destination, started);
    }

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<TimeModule>();
    }
}
