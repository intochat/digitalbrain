using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Time;

namespace DigitalBrain.Surfaces;

public sealed class CountdownSurface
{
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

        await new Shell.OpenHome().RunAsync(
            brain,
            shellName,
            sceneKey: "countdown",
            title: "Countdown",
            cancellationToken);

        var countdown = brain.Get<ICountdown>(countdownName);
        var destination = NeuronId.For<IScene>(brain.Owner, "countdown");
        return await countdown.Start(new StartCountdown(
            CommandId.New(),
            duration,
            destination));
    }
}
