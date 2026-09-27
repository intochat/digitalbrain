using DigitalBrain.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Client;

internal interface ILocalSignalHub
{
    void Subscribe(GrainId source, INeuronObserver observer);
    void Unsubscribe(GrainId source, INeuronObserver observer);
}
