using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    private CorrelationId? _clientEntryCorrelation;
    private readonly ConcurrentDictionary<Guid, CorrelationId> _clientStreamCorrelations = new();

    internal ClientEntryCorrelationScope EnterClientEntryCorrelation(CorrelationId correlation)
    {
        var previous = _clientEntryCorrelation;
        _clientEntryCorrelation = correlation;
        return new ClientEntryCorrelationScope(this, previous);
    }

    internal void RegisterClientStreamCorrelation(Guid enumerationId, CorrelationId correlation)
        => _clientStreamCorrelations[enumerationId] = correlation;

    internal bool TryGetClientStreamCorrelation(Guid enumerationId, out CorrelationId correlation)
        => _clientStreamCorrelations.TryGetValue(enumerationId, out correlation);

    internal void ForgetClientStreamCorrelation(Guid enumerationId)
        => _clientStreamCorrelations.TryRemove(enumerationId, out _);

    internal CorrelationId? AmbientClientEntryCorrelation => _clientEntryCorrelation;

    internal readonly struct ClientEntryCorrelationScope : IDisposable
    {
        private readonly Neuron _neuron;
        private readonly CorrelationId? _previous;

        public ClientEntryCorrelationScope(Neuron neuron, CorrelationId? previous)
        {
            _neuron = neuron;
            _previous = previous;
        }

        public void Dispose() => _neuron._clientEntryCorrelation = _previous;
    }
}
