using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

public interface IDigitalBrain
{
    OwnerId Owner { get; }

    Task ActivateAsync(CancellationToken cancellationToken = default);

    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Get is the ratified client verb for addressing a typed neuron by instance name.")]
    NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron;

    [EditorBrowsable(EditorBrowsableState.Never)]
    TNeuron GetGrainProxy<TNeuron>(string name = "default")
        where TNeuron : class, INeuron;

    Task SendAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken = default)
        where TNeuron : INeuron;

    Task SendAsync(NeuronId receiver, Synapse synapse, CancellationToken cancellationToken = default);

    Task EmitAsync(Synapse synapse, CancellationToken cancellationToken = default);
}
