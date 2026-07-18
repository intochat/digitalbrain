using TripRadar.Bot.Models;

namespace TripRadar.Bot.Auth;

public sealed record AuthSessionRequest(string InitData, long? ChatId);

public sealed record AuthSessionResponse(bool Success, string? Username, string? Token, string? RefreshToken, string? Error);

internal sealed class AuthSessionSyncHandler(
    ITripRadarTokenClient tokenClient,
    IUserSessionStore sessionStore)
{
    public async Task<BotResult<AuthSessionResponse>> HandleAsync(AuthSessionRequest request, CancellationToken ct = default)
    {
        var created = await tokenClient.CreateTelegramSessionAsync(request.InitData, ct);
        if (!created.Success)
            return BotResult<AuthSessionResponse>.Ok(new AuthSessionResponse(false, null, null, null, created.Error));

        var metadata = TokenClaimsReader.ReadUsernameAndExpiry(created.Value.AccessToken);
        if (!metadata.Success)
            return BotResult<AuthSessionResponse>.Ok(new AuthSessionResponse(false, null, null, null, metadata.Error));

        var username = metadata.Value.Username;

        sessionStore.Upsert(new UserSession(
            username,
            created.Value.AccessToken,
            created.Value.RefreshToken,
            metadata.Value.ExpiresAtUtc,
            DateTimeOffset.UtcNow));

        return BotResult<AuthSessionResponse>.Ok(new AuthSessionResponse(true, username, created.Value.AccessToken, created.Value.RefreshToken, null));
    }
}
