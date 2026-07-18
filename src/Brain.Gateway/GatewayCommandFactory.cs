using Brain.Contracts;

namespace Brain.Gateway;

public static class GatewayCommandFactory
{
    public static SynapseMetadata CreateMetadata(NeuronAddress source, Guid? commandId = null)
    {
        var id = commandId ?? Guid.NewGuid();
        return new SynapseMetadata(
            CommandId: id,
            EventId: id,
            CausationId: id,
            CorrelationId: id,
            OrganizationId: DevelopmentPrincipal.Current.OrganizationId,
            PrincipalId: DevelopmentPrincipal.Current.PrincipalId,
            SpaceId: DevelopmentPrincipal.Current.SpaceId,
            Source: source,
            SourceSequence: 0,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);
    }

    public static CommandSynapse<T> CreateCommand<T>(T payload, NeuronAddress source, Guid? commandId = null) =>
        new(CreateMetadata(source, commandId), payload);
}
