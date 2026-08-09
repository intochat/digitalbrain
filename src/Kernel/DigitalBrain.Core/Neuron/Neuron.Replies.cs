using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    protected Task ReplyAsync(Synapse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        if (_handling is null)
        {
            throw new InvalidOperationException(
                "ReplyAsync requires an active delivery context. Reply only from a HandleAsync turn.");
        }

        return FireAsync(response, [_handling.Caller], _handling, _handling.CorrelationId);
    }
}
