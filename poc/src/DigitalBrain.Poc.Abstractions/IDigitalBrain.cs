using System.Threading;
using System.Threading.Tasks;

namespace DigitalBrain.Poc.Abstractions;

public interface IDigitalBrain
{
    Task FireSynapse(Synapse synapse, CancellationToken cancellationToken = default);
}
