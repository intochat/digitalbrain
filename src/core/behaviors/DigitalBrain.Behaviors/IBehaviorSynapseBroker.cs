using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

internal interface IBehaviorSynapseBroker
{
    Task SendAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken)
        where TNeuron : INeuron;

    Task<TResponse> SendAsync<TNeuron, TResponse>(
        string name,
        RequestSynapse<TResponse> request,
        CancellationToken cancellationToken)
        where TNeuron : INeuron
        where TResponse : Synapse;
}
