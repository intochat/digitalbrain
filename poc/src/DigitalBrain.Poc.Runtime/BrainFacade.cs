using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Charting.Contracts;

namespace DigitalBrain.Poc.Runtime;

public sealed class BrainFacade
{
    private readonly Func<SynapseEnvelope, Task> _envelopeSink;

    public BrainFacade(Func<SynapseEnvelope, Task> envelopeSink)
    {
        _envelopeSink = envelopeSink ?? throw new ArgumentNullException(nameof(envelopeSink));
    }

    public IDigitalBrain ForCandidate(
        CandidateInvocationScope ownerScope,
        IReadOnlyCollection<Type> outputs) =>
        ForCandidate(ownerScope, outputs, []);

    public IDigitalBrain ForCandidate(
        CandidateInvocationScope ownerScope,
        IReadOnlyCollection<Type> outputs,
        IReadOnlyCollection<string> charts)
    {
        ArgumentNullException.ThrowIfNull(ownerScope);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(charts);
        return new CandidateBrain(ownerScope, outputs, charts, _envelopeSink);
    }

    private sealed class CandidateBrain(
        CandidateInvocationScope scope,
        IReadOnlyCollection<Type> outputs,
        IReadOnlyCollection<string> charts,
        Func<SynapseEnvelope, Task> sink) : IDigitalBrain
    {
        private readonly HashSet<Type> _outputs = new(outputs);
        private readonly HashSet<string> _charts = new(charts, StringComparer.Ordinal);
        private int _outputOrdinal;

        public Task FireSynapse(Synapse synapse, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(synapse);
            cancellationToken.ThrowIfCancellationRequested();
            var contractType = synapse.GetType();
            if (!_outputs.Contains(contractType))
            {
                throw new CapabilityDeniedException(contractType);
            }

            var targetScope = synapse is AddChartPoint addChartPoint
                ? addChartPoint.ChartId
                : null;
            if (targetScope is not null && !_charts.Contains(targetScope))
            {
                throw new CapabilityDeniedException(contractType, targetScope);
            }

            var envelope = targetScope is null
                ? SynapseEnvelope.CandidateLocal(
                    scope,
                    synapse,
                    ContractAlias.For(contractType),
                    _outputOrdinal++)
                : SynapseEnvelope.CandidateTrustedTarget(
                    scope,
                    synapse,
                    ContractAlias.For(contractType),
                    _outputOrdinal++,
                    targetScope);
            return sink(envelope);
        }
    }
}
