using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Scripting.Startup;

internal sealed class DigitalBrainBehaviorAdmissionSource(IDigitalBrain brain) : IBehaviorAdmissionSource
{
    public async IAsyncEnumerable<AdmittedBehavior> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var behaviors = brain.Get<IBehaviors>();
        await foreach (var delivery in JournalWatch.OutgoingAsync(
            behaviors.ReadJournalAsync,
            behaviors.WatchJournalAsync,
            cancellationToken))
        {
            if (delivery.Signal is BehaviorAdmitted admitted)
            {
                yield return new AdmittedBehavior(admitted.Name, admitted.Source);
            }
        }
    }
}
