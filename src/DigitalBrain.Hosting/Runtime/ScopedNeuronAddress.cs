namespace DigitalBrain;

internal readonly record struct ScopedNeuronAddress
{
    internal ScopedNeuronAddress(ScopeKey scope, NeuronId neuron)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(neuron.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(neuron.Name);
        Scope = scope;
        Neuron = neuron;
    }

    internal ScopeKey Scope { get; }

    internal NeuronId Neuron { get; }
}
