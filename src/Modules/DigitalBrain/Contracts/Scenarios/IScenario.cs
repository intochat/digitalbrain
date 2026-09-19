using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Scenarios;

[Alias("db.v3.scenario")]
public interface IScenario : IGrainWithStringKey
{
    [Alias(nameof(Bind))]
    Task Bind(INeuron source, string signalType, INeuron target);

    [Alias(nameof(Unbind))]
    Task Unbind(INeuron source, string signalType, INeuron target);

    [ReadOnly, Alias(nameof(Read))]
    Task<IReadOnlyList<Synapse>> Read();

    [Alias(nameof(Route))]
    Task<int> Route(SignalDelivery delivery, CancellationToken cancellationToken = default);
}
