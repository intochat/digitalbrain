using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public interface IConversationLifecycleState
{
    Task<InoConversationSnapshot> EnsureConversationAsync(
        RuntimeRequestContext context,
        string conversationId,
        CancellationToken cancellationToken);

    Task TombstoneConversationAsync(
        RuntimeRequestContext context,
        string conversationId,
        string reason,
        CancellationToken cancellationToken);
}

public interface IActiveConversationFeed
{
    Task<string> ResolveActiveConversationIdAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken);

    Task<bool> TryActivateConversationAsync(
        RuntimeRequestContext context,
        string expectedConversationId,
        InoConversationSnapshot conversation,
        string projectionId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);
}

public sealed record ConversationLifecycleResult(
    string ConversationId,
    bool Activated,
    string? DeletedConversationId = null);

public sealed class ConversationLifecycleCoordinator(
    IConversationLifecycleState conversations,
    IActiveConversationFeed feed,
    TimeProvider timeProvider)
{
    private const string DeleteReason = "user-requested-conversation-delete";

    public async Task<ConversationLifecycleResult> CreateAsync(
        RuntimeRequestContext context,
        string expectedConversationId,
        string lifecycleOperationId,
        CancellationToken cancellationToken = default)
    {
        DemandLifecycleRequest(expectedConversationId, lifecycleOperationId);
        var conversationId = DeriveConversationId(context, "create", lifecycleOperationId);
        var conversation = await conversations.EnsureConversationAsync(
            context,
            conversationId,
            cancellationToken).ConfigureAwait(false);
        var activated = await feed.TryActivateConversationAsync(
            context,
            expectedConversationId,
            conversation,
            ProjectionId("create", lifecycleOperationId),
            timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return new(conversationId, activated);
    }

    public async Task<ConversationLifecycleResult> DeleteAsync(
        RuntimeRequestContext context,
        string expectedConversationId,
        string lifecycleOperationId,
        CancellationToken cancellationToken = default)
    {
        DemandLifecycleRequest(expectedConversationId, lifecycleOperationId);
        await conversations.TombstoneConversationAsync(
            context,
            expectedConversationId,
            DeleteReason,
            cancellationToken).ConfigureAwait(false);

        var replacementId = DeriveConversationId(context, "delete-replacement", lifecycleOperationId);
        var replacement = await conversations.EnsureConversationAsync(
            context,
            replacementId,
            cancellationToken).ConfigureAwait(false);
        var activated = await feed.TryActivateConversationAsync(
            context,
            expectedConversationId,
            replacement,
            ProjectionId("delete", lifecycleOperationId),
            timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return new(replacementId, activated, expectedConversationId);
    }

    internal static string DeriveConversationId(
        RuntimeRequestContext context,
        string purpose,
        string lifecycleOperationId)
    {
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Length > 32 || purpose.Any(char.IsControl))
            throw new ArgumentException("A bounded lifecycle purpose is required.", nameof(purpose));
        DemandOperationId(lifecycleOperationId);
        return "ino-" + Hash(RequestScope.Id(context) + "\0" + purpose + "\0" + lifecycleOperationId);
    }

    private static string ProjectionId(string purpose, string lifecycleOperationId) =>
        "conversation-" + purpose + "-" + Hash(lifecycleOperationId)[..32];

    private static void DemandLifecycleRequest(string expectedConversationId, string lifecycleOperationId)
    {
        ConversationStateClient.DemandConversationId(expectedConversationId);
        DemandOperationId(lifecycleOperationId);
    }

    private static void DemandOperationId(string lifecycleOperationId)
    {
        if (string.IsNullOrWhiteSpace(lifecycleOperationId) || lifecycleOperationId.Length > 256 ||
            lifecycleOperationId.Any(char.IsControl))
            throw new ArgumentException("A bounded lifecycle operation id is required.", nameof(lifecycleOperationId));
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
