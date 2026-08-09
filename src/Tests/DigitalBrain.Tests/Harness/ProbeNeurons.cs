using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Tests.Harness;

[GrainType("probesource")]
internal sealed class ProbeSource : Neuron, IProbeSource
{
    public Task HandleAsync(Poke synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        return EmitAsync(new ProbeFact(synapse.Text));
    }
}

[GrainType("probesink")]
internal sealed class ProbeSink : Neuron, IProbeSink;

[GrainType("probeecho")]
internal sealed class ProbeEcho : Neuron, IProbeEcho
{
    public Task HandleAsync(Poke synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        return EmitAsync(new ProbeFact(synapse.Text));
    }

    protected override Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
        => synapse is ProbeFact fact
            ? EmitAsync(new ProbeFact(fact.Text))
            : Task.CompletedTask;
}
