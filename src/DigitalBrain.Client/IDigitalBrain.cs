using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

public interface IDigitalBrain
{
    OwnerId Owner { get; }

    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Get is the ratified client verb for addressing a typed neuron by instance name.")]
    T Get<T>()
        where T : class, INeuron;

    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Get is the ratified client verb for addressing a typed neuron by instance name.")]
    T Get<T>(string name)
        where T : class, INeuron;

    Task SendAsync<TNeuron>(Synapse synapse)
        where TNeuron : INeuron;

    Task SendAsync<TNeuron>(string name, Synapse synapse)
        where TNeuron : INeuron;

    Task SendAsync(NeuronId receiver, Synapse synapse);

    Task EmitAsync(Synapse synapse);
}
