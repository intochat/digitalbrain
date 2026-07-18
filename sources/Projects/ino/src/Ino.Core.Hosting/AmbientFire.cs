using Ino.Core;
using Microsoft.Extensions.Logging;

namespace Ino.Core.Hosting;

public sealed class AmbientFire(
    IFirePort firePort,
    DomainId thisSilo,
    ILogger<AmbientFire> logger) : IAmbientFire
{
    public Task<NeuronResult> FireAsync<T>(T synapse, CorrelationId? correlationId = null, CancellationToken ct = default)
        where T : ISynapse
    {
        var ctx = BuildContext(correlationId);
        return firePort.Fire(synapse, ctx, ct);
    }

    public Task FireBroadcastAsync<T>(T synapse, CorrelationId? correlationId = null, CancellationToken ct = default)
        where T : ISynapse
    {
        var ctx = BuildContext(correlationId);
        return firePort.FireBroadcast(synapse, ctx, ct);
    }

    private NeuronContext BuildContext(CorrelationId? correlationId)
    {
        return new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: correlationId ?? CorrelationId.New(),
            Source: new Caller.Ambient(thisSilo),
            SourceStream: new StreamKey($"<ambient:{thisSilo.Value}>"))
        {
            FirePort = firePort,
            Logger = logger,
        };
    }
}
