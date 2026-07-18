using Ino.Core;

namespace Ino.Core.Hosting;

public interface IAmbientFire
{
    Task<NeuronResult> FireAsync<T>(T synapse, CorrelationId? correlationId = null, CancellationToken ct = default) where T : ISynapse;
    Task FireBroadcastAsync<T>(T synapse, CorrelationId? correlationId = null, CancellationToken ct = default) where T : ISynapse;
}
