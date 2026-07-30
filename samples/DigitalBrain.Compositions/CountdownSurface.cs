using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
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

        var shell = brain.Get<IShell>(shellName);
        await shell.Open(new OpenScene(CommandId.New(), SceneKey, SceneTitle));

        var countdown = brain.Get<ICountdown>(countdownName);
        var destination = NeuronId.For<IScene>(brain.Owner, SceneKey);
        return await countdown.Start(new StartCountdown(CommandId.New(), duration, destination));
    }
}
