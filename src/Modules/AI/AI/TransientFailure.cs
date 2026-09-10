using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;

namespace DigitalBrain.AI;

// A neuron that could not reach another one, or could not persist, has not failed its work:
// the silo was going down or the target was moving. Answering the asker would end the
// conversation over something the at-least-once drain retries successfully.
internal static class TransientFailure
{
    internal static bool Covers(Exception failure)
        => failure is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.Any(Covers)
            : failure is OrleansException or NeuronBusyException or NeuronRecoveringException or NeuronPersistenceException;
}
