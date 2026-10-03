using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Contracts.Signals;

public interface ILocalSignalHub
{
    void Subscribe(GrainId source, INeuronObserver observer);
    void Unsubscribe(GrainId source, INeuronObserver observer);
}
