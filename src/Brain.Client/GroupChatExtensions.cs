using Brain.Contracts;
using DigitalBrain.AI;
using Orleans.Runtime;

namespace Brain.Client;

public static class GroupChatExtensions
{
    public static Task<CommandReceipt> StartDiscussion(
        this IGroupChat chat,
        string topic,
        IGpt56 gpt,
        IGrok45 grok,
        OrganizationId organizationId,
        PrincipalId principalId,
        SpaceId spaceId)
    {
        var commandId = Guid.NewGuid();
        var source = NeuronAddress.Parse(((IAddressable)chat).GetGrainId().Key.ToString()!);
        var metadata = new SynapseMetadata(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: organizationId,
            PrincipalId: principalId,
            SpaceId: spaceId,
            Source: source,
            SourceSequence: 0,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

        var payload = new StartDiscussion(
            topic,
            ((IAddressable)gpt).GetGrainId().Key.ToString()!,
            ((IAddressable)grok).GetGrainId().Key.ToString()!);

        return chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(metadata, payload));
    }
}
