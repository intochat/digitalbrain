namespace DigitalBrain.Runtime.Runtime;

public interface IInterpretedNeuronRegistry
{
    Task RegisterDynamicAsync(InterpretedNeuronRegistration registration);
    bool TryGet(string fqn, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out InterpretedNeuronRegistration? registration);
    System.Collections.Generic.IReadOnlyCollection<string> RegisteredFqns { get; }
}
