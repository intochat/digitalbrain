using DigitalBrain.Client;

namespace DigitalBrainConsole;

public static class ConsoleSession
{
    public static async Task RunAsync(IDigitalBrain brain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);

        await brain.GetGrainProxy<IConsole>().Attach(cancellationToken).ConfigureAwait(false);

        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = cancellationToken.Register(() => exit.TrySetResult());
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exit.TrySetResult();
        };

        Console.WriteLine("digitalbrain ready  |  Ctrl+C to stop");
        await exit.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
