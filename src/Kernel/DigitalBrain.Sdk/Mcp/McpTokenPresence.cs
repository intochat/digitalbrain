using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Modules.Sdk;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpTokenPresence
{
    internal static bool IsMissingOrExpired(
        Func<byte[]?> read,
        IDurablePayloadProtector protector,
        string purpose,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(time);

        if (read() is not { Length: > 0 } protectedTokens)
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

    internal static bool IsMissingOrExpired(
        IDurableValue<byte[]> tokenState,
        IDurablePayloadProtector protector,
        string purpose,
        TimeProvider time)
        => IsMissingOrExpired(
            () => tokenState.Value is { Length: > 0 } bytes ? bytes : null,
            protector,
            purpose,
            time);

    internal static bool IsMissingOrExpired(
        PrincipalTokenSlot slot,
        IDurablePayloadProtector protector,
        string purpose,
        TimeProvider time)
        => IsMissingOrExpired(slot.Read, protector, purpose, time);

    // Read tokens even when access is expired — needed for refresh_token grant (S15).
    internal static bool TryReadTokens(
        PrincipalTokenSlot slot,
        IDurablePayloadProtector protector,
        string purpose,
        out TokenContainer tokens)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        tokens = null!;

        if (slot.Read() is not { Length: > 0 } protectedTokens)
        {
            return false;
        }

        try
        {
            var serialized = protector.Unprotect(purpose, protectedTokens);
            var parsed = JsonSerializer.Deserialize<TokenContainer>(serialized);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.AccessToken))
            {
                return false;
            }

            tokens = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Integration-record purpose: tokens are keyed by verified principal (User scope)
    // or workspace subject — never by bare neuron/server name alone.
    internal static string Purpose(string provider, IntegrationScope scope, string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        return $"integration/{scope.ToString().ToLowerInvariant()}/{provider}/{subjectId}";
    }

    internal static string SubjectKey(ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.PrincipalId.Value.ToString("N");
    }

    internal static Integration UserIntegration(
        string provider,
        ActorContext actor,
        string[] grantedScopes,
        string? externalAccount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grantedScopes);

        var subjectId = SubjectKey(actor);
        return new Integration(
            provider,
            IntegrationScope.User,
            subjectId,
            externalAccount,
            grantedScopes,
            Purpose(provider, IntegrationScope.User, subjectId));
    }

    internal static async ValueTask StoreAsync(
        PrincipalTokenSlot slot,
        Func<ValueTask> commit,
        IDurablePayloadProtector protector,
        string purpose,
        TokenContainer tokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();

        var cache = new DurableMcpTokenCache(slot, commit, protector, purpose);
        await cache.StoreTokensAsync(tokens, cancellationToken).ConfigureAwait(false);
    }
}
