namespace DigitalBrain;

internal sealed class OrleansWorkspaceChannelIssuer(IGrainFactory grains)
{
    internal WorkspaceChannel Open(
        ScopeKey scope,
        SynapseSource source,
        IReadOnlySet<Type> permittedIngressSynapses)
        => new OrleansWorkspaceChannel(scope, source, grains, permittedIngressSynapses);
}
