namespace DigitalBrain.Runtime.Runtime;

// E-SDK #63. Pluggable discovery for production interpreted neurons —
// the neuron by which future sources (Creator-authored .ino persistence,
// filesystem scanners, marketplace install) hand descriptors to the
// kernel at silo start. Implementations register via DI; the
// InterpretedNeuronRegistry IHostedService aggregates all of them.
public interface IInterpretedNeuronSource
{
    Task<IReadOnlyList<InterpretedNeuronRegistration>> DiscoverAsync(CancellationToken cancellationToken);
}
