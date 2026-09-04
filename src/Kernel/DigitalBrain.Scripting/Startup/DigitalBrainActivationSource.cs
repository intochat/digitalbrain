using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Scripting.Startup;

internal sealed class DigitalBrainActivationSource(IDigitalBrain brain) : IStartupActivationSource
{
    public async IAsyncEnumerable<StartupActivation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var delivery in JournalWatch.OutgoingAsync(
            brain.ReadJournalAsync,
            brain.WatchJournalAsync,
            cancellationToken))
        {
            if (delivery.Signal is DigitalBrainActivated { Owner: var owner } && owner == brain.Owner)
            {
                yield return new StartupActivation(owner.Value, delivery.SignalId.Value.ToString("D"));
            }
        }
    }
}
