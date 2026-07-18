using DigitalBrain.Kernel.Contracts.Runtime;

namespace DigitalBrain.Mcp;

public sealed record McpDevSessionRequest(string Username, string Password);
public sealed record McpDevSessionReply(
    string AccessToken,
    string RefreshToken,
    long AccessExpiresAtUnixMs,
    long RefreshExpiresAtUnixMs,
    string SessionId,
    string OwnerId,
    string ActorId,
    string Audience);

public static class McpDevSessionEndpoint
{
    public static async Task<IResult> CreateAsync(
        McpDevSessionRequest request,
        UiDevelopmentLoginAuthenticator developmentLogin,
        RuntimeSessionAuthority sessions,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            !developmentLogin.TryAuthenticate(request.Username, request.Password, out var context))
            return Results.Unauthorized();
        var mcpContext = context with
        {
            Grants = context.Grants.Append("brain.interact").ToHashSet(StringComparer.Ordinal)
        };
        var issued = await sessions.CreateAsync(mcpContext, TimeSpan.FromHours(8), SessionAudiences.Mcp, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(new McpDevSessionReply(
            issued.Pair.AccessToken,
            issued.Pair.RefreshToken,
            issued.Pair.AccessExpiresAt.ToUnixTimeMilliseconds(),
            issued.Pair.RefreshExpiresAt.ToUnixTimeMilliseconds(),
            issued.Context.SessionId.Value,
            issued.Context.OwnerId.Value,
            issued.Context.ActorId.Value,
            SessionAudiences.Mcp));
    }
}
