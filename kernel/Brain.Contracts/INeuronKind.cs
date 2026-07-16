namespace Brain.Contracts;

public sealed record NeuronContext(NeuronAddress Address, string CallerKey, long Revision, IReadOnlyList<SynapseRecord> Synapses, IReadOnlyList<NeuronEvent> Journal);

public sealed record EffectProposal(string Provider, string PayloadJson, string PayloadDigest);

public sealed record KindResult(string OutputJson, IReadOnlyList<(string Kind, string PayloadJson)> Events, EffectProposal? Effect = null, SynapseRecord? Synapse = null);

public interface INeuronKind
{
    string Kind { get; }
    string[] Contracts { get; }
    ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation);
    string Project(NeuronContext context, string projection);
}
