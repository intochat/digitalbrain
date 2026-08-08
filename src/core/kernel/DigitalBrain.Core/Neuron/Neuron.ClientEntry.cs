using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    private readonly ConcurrentDictionary<Guid, CorrelationId> _clientStreamCorrelations = new();

    internal ClientEntryCorrelationScope EnterClientEntryCorrelation(CorrelationId correlation)
    {
        var previous = AmbientClientEntryCorrelation;
        AmbientClientEntryCorrelation = correlation;
        return new ClientEntryCorrelationScope(this, previous);
    }

    internal void RegisterClientStreamCorrelation(Guid enumerationId, CorrelationId correlation)
        => _clientStreamCorrelations[enumerationId] = correlation;

    internal bool TryGetClientStreamCorrelation(Guid enumerationId, out CorrelationId correlation)
        => _clientStreamCorrelations.TryGetValue(enumerationId, out correlation);

    internal void ForgetClientStreamCorrelation(Guid enumerationId)
        => _clientStreamCorrelations.TryRemove(enumerationId, out _);

    internal CorrelationId? AmbientClientEntryCorrelation { get; private set; }

    internal readonly struct ClientEntryCorrelationScope : IDisposable
    {
        private readonly Neuron _neuron;
        private readonly CorrelationId? _previous;

        public ClientEntryCorrelationScope(Neuron neuron, CorrelationId? previous)
        {
            _neuron = neuron;
            _previous = previous;
        }

        public void Dispose() => _neuron.AmbientClientEntryCorrelation = _previous;
    }
}
