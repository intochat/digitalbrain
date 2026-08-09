using System.Threading;
using System.Threading.Tasks;

namespace DigitalBrain.Poc.Abstractions;

public interface IHandle<TSynapse>
    where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}
