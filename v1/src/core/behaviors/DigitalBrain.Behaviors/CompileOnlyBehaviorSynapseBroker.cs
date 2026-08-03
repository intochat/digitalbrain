using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

internal sealed class CompileOnlyBehaviorSynapseBroker : IBehaviorSynapseBroker
{
    internal static CompileOnlyBehaviorSynapseBroker Instance { get; } = new();

    private CompileOnlyBehaviorSynapseBroker()
    {
    }

    public Task SendAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken)
        where TNeuron : INeuron
        => Task.FromException(
            new InvalidOperationException(
                "Behavior synapse delivery is supplied by the isolated worker broker."));

    public Task EmitAsync(Synapse fact, CancellationToken cancellationToken)
        => Task.FromException(
            new InvalidOperationException(
                "Behavior fact emission is supplied by the isolated worker broker."));

    public Task<TResponse> SendAsync<TNeuron, TResponse>(
        string name,
        RequestSynapse<TResponse> request,
        CancellationToken cancellationToken)
        where TNeuron : INeuron
        where TResponse : Synapse
        => Task.FromException<TResponse>(
            new InvalidOperationException(
                "Behavior synapse delivery is supplied by the isolated worker broker."));
}
