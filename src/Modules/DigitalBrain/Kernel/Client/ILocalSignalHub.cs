using DigitalBrain.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Core;

public interface ILocalSignalHub
{
    void Subscribe(GrainId source, INeuronObserver observer);
    void Unsubscribe(GrainId source, INeuronObserver observer);
}
