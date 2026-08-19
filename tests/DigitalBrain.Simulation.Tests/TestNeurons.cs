using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrain.Simulation.Tests;

[GenerateSerializer]
[Alias(AliasName)]
public sealed record EmitPing([property: Id(0)] string Note) : Synapse
{
    public const string AliasName = "test.emit-ping";
}

[GenerateSerializer]
[Alias(AliasName)]
public sealed record Pinged([property: Id(0)] string Note) : Synapse
{
    public const string AliasName = "test.pinged";
}

[Alias("test.pinger")]
public interface IPingerNeuron : INeuron, IHandle<EmitPing>;

[Alias("test.echo")]
public interface IEchoNeuron : INeuron, IHandle<Pinged>;

// Emits Pinged when poked; the wire-delivery pin routes that emission through the Brain.
[GrainType("pingerneuron")]
public sealed class PingerNeuron : Neuron, IPingerNeuron
{
    public Task HandleAsync(EmitPing synapse, CancellationToken cancellationToken)
        => EmitAsync(new Pinged(synapse.Note));
}

[GrainType("echoneuron")]
public sealed class EchoNeuron : Neuron, IEchoNeuron
{
    public Task HandleAsync(Pinged synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
