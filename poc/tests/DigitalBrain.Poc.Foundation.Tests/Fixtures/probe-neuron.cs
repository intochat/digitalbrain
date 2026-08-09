#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net11.0
#:property OutputType=Library
#:property PublishAot=false
#:property ImplicitUsings=disable
#:property AssemblyName=DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc
#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj

using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.Abstractions;
using Orleans;

namespace DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc;

[GenerateSerializer]
[Alias("db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1")]
public sealed record ProbeSynapse([property: Id(0)] string Value) : Synapse;

public sealed class ProbeEmitterNeuron : Neuron, IHandle<ProbeIngress>
{
    public ProbeEmitterNeuron(IDigitalBrain digitalBrain)
        : base(digitalBrain)
    {
    }

    public Task HandleAsync(ProbeIngress synapse, CancellationToken cancellationToken) =>
        DigitalBrain.FireSynapse(new ProbeSynapse(synapse.Value), cancellationToken);
}

public sealed class ProbeNeuron : Neuron<string>, IHandle<ProbeSynapse>
{
    public ProbeNeuron(IDigitalBrain digitalBrain, IDurableState<string> durableState)
        : base(digitalBrain, durableState)
    {
    }

    public Task HandleAsync(ProbeSynapse synapse, CancellationToken cancellationToken)
    {
        DurableState.Replace(synapse.Value);
        return Task.CompletedTask;
    }
}
