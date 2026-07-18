using Ino.Core;
using Ino.Core.Hosting;
using Ino.Kernel.Contracts;
using Orleans;

namespace Ino.Kernel;

public sealed class SystemEcho : Grain, INeuron<EchoRequest>
{
    public Task<NeuronResult> HandleAsync(EchoRequest synapse, NeuronContext ctx, CancellationToken ct)
    {
        var response = new EchoResponse($"[from system] {synapse.Message}", RuntimeIdentity);
        return Task.FromResult(NeuronResult.Ok().With(response));
    }
}
