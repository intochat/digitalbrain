using Brain.Contracts;
using DigitalBrain.AI;

namespace Brain.Gateway;

public static class UiActionAdapter
{
    private const string GroupChatContractId = "chat.group.v1";

    public static Task ApplyAsync(
        string surfaceGrainKey,
        string actionId,
        long expectedRevision,
        DevelopmentPrincipal principal,
        Func<string, IGroupChat> resolveGroupChat)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resolveGroupChat);

        var surfaceAddress = NeuronAddress.Parse(surfaceGrainKey);
        if (!string.Equals(surfaceAddress.ContractId, GroupChatContractId, StringComparison.Ordinal)
            || surfaceAddress.OrganizationId != principal.OrganizationId
            || surfaceAddress.SpaceId != principal.SpaceId)
        {
            throw new InvalidOperationException("Surface is not authorized for UI action.");
        }

        var commandId = Guid.NewGuid();
        var metadata = new SynapseMetadata(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: principal.OrganizationId,
            PrincipalId: principal.PrincipalId,
            SpaceId: principal.SpaceId,
            Source: surfaceAddress,
            SourceSequence: 0,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

        var groupChat = resolveGroupChat(surfaceGrainKey);
        return groupChat.ApplyUiActionAsync(
            new CommandSynapse<UiActionRequest>(
                metadata,
                new UiActionRequest(actionId, expectedRevision)));
    }
}
