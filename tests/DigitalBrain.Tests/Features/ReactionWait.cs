using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Tests;

internal static class ReactionWait
{
    internal static async Task<SignalDelivery> ForSignalAsync(INeuron neuron, string type, CancellationToken cancellationToken)
    {
        SignalDelivery? signal = null;
        await UntilAsync(async () =>
        {
            var journal = await neuron.ReadJournal(JournalKind.Outgoing, 0);
            signal = journal.Delta.FirstOrDefault(entry => entry.Signal.Type == type);
            return signal is not null;
        }, cancellationToken);
        return signal!;
    }

    internal static async Task UntilAsync(Func<Task<bool>> completed, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await completed().WaitAsync(timeout.Token))
        {
            // This waits for reaction scheduler latency, not domain time; a fake clock cannot remove it.
            await Task.Delay(20, timeout.Token);
        }
    }
}
