using System.Text.Json;
using DigitalBrain.Security;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpTokenPresence
{
    internal static bool IsMissingOrExpired(
        IDurableValue<byte[]> tokenState,
        IDurablePayloadProtector protector,
        string purpose,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(time);

        if (tokenState.Value is not { Length: > 0 } protectedTokens)
        {
            return true;
        }

        var serialized = protector.Unprotect(purpose, protectedTokens);
        var tokens = JsonSerializer.Deserialize<TokenContainer>(serialized);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            return true;
        }

        if (tokens.ExpiresIn is not { } lifetimeSeconds || lifetimeSeconds <= 0)
        {
            return false;
        }

        var expiresAt = tokens.ObtainedAt.AddSeconds(lifetimeSeconds);
        return expiresAt <= time.GetUtcNow();
    }

    internal static string Purpose(string serverKey, string durableIdentity)
        => $"mcp/oauth/{serverKey}/{durableIdentity}";

    internal static async ValueTask StoreAsync(
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        IDurablePayloadProtector protector,
        string purpose,
        TokenContainer tokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();

        var cache = new DurableMcpTokenCache(tokenState, commit, protector, purpose);
        await cache.StoreTokensAsync(tokens, cancellationToken).ConfigureAwait(false);
    }
}
