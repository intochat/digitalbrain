using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Scripting.Startup;

internal sealed class DigitalBrainBehaviorAdmissionSource(IDigitalBrain brain) : IBehaviorAdmissionSource
{
    public async IAsyncEnumerable<AdmittedBehavior> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var behaviors = brain.Get<IBehaviors>();
        var initial = await behaviors.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken);

        foreach (var delivery in initial.Delta)
        {
            if (TryCreate(delivery.Signal, out var admitted))
            {
                yield return admitted;
            }
        }

        await foreach (var page in behaviors
            .WatchJournalAsync(JournalKind.Outgoing, initial.ResumeSequence, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (var delivery in page.Delta)
            {
                if (TryCreate(delivery.Signal, out var admitted))
                {
                    yield return admitted;
                }
            }
        }
    }

    private static bool TryCreate(Signal signal, out AdmittedBehavior admitted)
    {
        if (signal is BehaviorAdmitted admittedSignal)
        {
            admitted = new AdmittedBehavior(admittedSignal.Name, admittedSignal.Source);
            return true;
        }

        admitted = null!;
        return false;
    }
}
