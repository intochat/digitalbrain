using Core.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public sealed class UserContextProvider(
    IGrainFactory grainFactory,
    ILogger<UserContextProvider>? logger = null)
    : MessageAIContextProvider
{
    protected override async ValueTask<IEnumerable<Microsoft.Extensions.AI.ChatMessage>> ProvideMessagesAsync(
        MessageAIContextProvider.InvokingContext context, CancellationToken cancellationToken = default)
    {
        var userId = ContextProviderIdentity.ReadUserId();
        if (userId is null)
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

        try
        {
            var userProfile = grainFactory.GetGrain<IUserProfile>(userId);
            var prefs = await userProfile.GetPreferences(cancellationToken);
            if (prefs.Count == 0)
                return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

            var lines = new List<string> { "## User profile" };
            foreach (var kvp in prefs)
                lines.Add($"- {kvp.Key}: {kvp.Value}");

            return new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, string.Join("\n", lines)) };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "UserContextProvider failed for user {UserId}", userId);
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();
        }
    }
}
