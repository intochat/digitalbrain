using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Scripting.Startup;

internal sealed class DigitalBrainActivationSource(IDigitalBrain brain) : IStartupActivationSource
{
    public async IAsyncEnumerable<StartupActivation> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var initial = await brain.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken);

        foreach (var delivery in initial.Delta)
        {
            if (TryCreateActivation(delivery, out var activation))
            {
                yield return activation;
            }
        }

        await foreach (var page in brain
            .WatchJournalAsync(JournalKind.Outgoing, initial.ResumeSequence, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (var delivery in page.Delta)
            {
                if (TryCreateActivation(delivery, out var activation))
                {
                    yield return activation;
                }
            }
        }
    }

    private bool TryCreateActivation(SignalDelivery delivery, out StartupActivation activation)
    {
        if (delivery.Signal is DigitalBrainActivated { Owner: var owner } && owner == brain.Owner)
        {
            activation = new StartupActivation(owner.Value, delivery.SignalId.Value.ToString("D"));
            return true;
        }

        activation = default!;
        return false;
    }
}
