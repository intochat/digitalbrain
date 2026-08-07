using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Shell;
using DigitalBrain.Time;

namespace DigitalBrain.Compositions;

public sealed class CountdownSurface
{
    public const string SceneKey = "countdown";
    public const string SceneTitle = "Countdown";

    public async Task<CountdownSnapshot> RunAsync(
        IDigitalBrain brain,
        string shellName,
        string countdownName,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentException.ThrowIfNullOrWhiteSpace(countdownName);
        cancellationToken.ThrowIfCancellationRequested();

        await brain.SendAsync<IShell>(
            shellName,
            new OpenScene(CommandId.New(), SceneKey, SceneTitle),
            cancellationToken);

        var countdown = brain.GetGrainProxy<ICountdown>(countdownName);
        var destination = NeuronId.For<IScene>(brain.Owner, SceneKey);
        return await countdown.Start(new StartCountdown(CommandId.New(), duration, destination));
    }
}
