using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

[GrainType("behaviors")]
internal sealed class BehaviorsNeuron(NeuronRuntime runtime) : Neuron(runtime), IBehaviors
{
    public async Task HandleAsync(AdmitBehavior signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        await RecordOutgoingAsync(new BehaviorAdmitted(signal.Name, signal.Source))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
