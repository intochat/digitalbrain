using Brain.Contracts;

namespace Brain.Mcp;

public interface ITypedCommandPath
{
    CommandSynapse<T> CreateCommand<T>(T payload, NeuronAddress source, Guid? commandId = null);
}

public sealed class JournalOutboxFeedCommandPath : ITypedCommandPath
{
    public static readonly OrganizationId OrganizationId = new("dev-organization");
    public static readonly PrincipalId PrincipalId = new("dev-principal");
    public static readonly SpaceId SpaceId = new("dev-space");

    public CommandSynapse<T> CreateCommand<T>(T payload, NeuronAddress source, Guid? commandId = null)
    {
        var id = commandId ?? Guid.NewGuid();
        var metadata = new SynapseMetadata(
            CommandId: id,
            EventId: id,
            CausationId: id,
            CorrelationId: id,
            OrganizationId: OrganizationId,
            PrincipalId: PrincipalId,
            SpaceId: SpaceId,
            Source: source,
            SourceSequence: 0,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);
        return new CommandSynapse<T>(metadata, payload);
    }
}
