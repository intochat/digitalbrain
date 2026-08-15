namespace DigitalBrain.Abstractions.Neurons;

[ClientEntryPoint]
[Alias("DigitalBrain.Abstractions.IDigitalBrainNeuron")]
public interface IDigitalBrainNeuron : INeuron
{
    const string GrainTypeName = "digitalbrain";
    const string InstanceName = "brain";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    [Alias(nameof(Activate))]
    Task Activate();
}
