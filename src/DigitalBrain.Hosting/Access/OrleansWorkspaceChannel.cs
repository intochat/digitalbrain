namespace DigitalBrain;

internal sealed class OrleansWorkspaceChannel(
    ScopeKey scope,
    SynapseSource source,
    IGrainFactory grains,
    IReadOnlySet<Type> permittedIngressSynapses) : WorkspaceChannel
{
    public SynapsePublisher Publisher { get; } = new OrleansSynapsePublisher(
        grains,
        new ScopedNeuronAddress(scope, SynapseSourceIdentity.For(source)),
        permittedIngressSynapses);

    public JournalReader Journal { get; } = new OrleansJournalReader(grains, scope);
}
