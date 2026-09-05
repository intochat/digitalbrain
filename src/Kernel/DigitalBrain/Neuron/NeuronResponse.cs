using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal static class NeuronResponse
{
    internal static TResponse Read<TResponse>(
        NeuronId receiver,
        SignalDelivery request,
        long cursor,
        JournalRead journal)
        where TResponse : Signal
    {
        foreach (var reply in journal.Delta)
        {
            if (reply.Sequence > cursor
                && reply.Caller == receiver
                && reply.CausationId == request.SignalId
                && reply.Signal is TResponse response)
            {
                return response;
            }
        }

        if (journal.ResetSnapshot is not null)
        {
            throw new InvalidOperationException(
                $"Neuron '{receiver}' compacted its outgoing journal past sequence {cursor} before "
                + $"the '{typeof(TResponse).Name}' reply to request '{request.SignalId}' could be read.");
        }

        throw new InvalidOperationException(
            $"Neuron '{receiver}' handled request '{request.SignalId}' without recording "
            + $"a '{typeof(TResponse).Name}' reply with its exact causation ID.");
    }
}
