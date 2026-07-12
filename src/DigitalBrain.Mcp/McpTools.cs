using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class McpAuthority(
    IHttpContextAccessor http,
    RuntimeRequestAuthenticator authentication,
    IConfiguration configuration)
{
    public async Task<RuntimeRequestContext> RequireContextAsync(CancellationToken cancellationToken = default)
    {
        _ = SessionAudiences.RequireFixedMcp(configuration["DigitalBrain:Runtime:Mcp:Audience"]);
        var httpContext = http.HttpContext;
        if (httpContext is null)
            throw new UnauthorizedAccessException("Authenticated MCP session required.");
        return await authentication.AuthenticateMcpAsync(httpContext, cancellationToken).ConfigureAwait(false)
               ?? throw new UnauthorizedAccessException("Authenticated MCP session required.");
    }

    internal static void DemandGrant(RuntimeRequestContext context, string grant)
    {
        if (!context.Grants.Contains(grant))
            throw new UnauthorizedAccessException("The authenticated principal lacks the required capability.");
    }
}

[McpServerToolType]
public sealed class McpConversationTools(
    McpAuthority authority,
    McpInoCommandHandler conversation)
{
    [McpServerTool(Name = "ino_interact"), Description("Durably accept an authenticated, idempotent INO interaction for the current workspace.")]
    public async Task<object> InoInteractAsync(
        string commandId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var context = await authority.RequireContextAsync(cancellationToken).ConfigureAwait(false);
        McpAuthority.DemandGrant(context, "brain.interact");
        var commandContext = context with
        {
            IdempotencyKey = commandId
        };
        var receipt = await conversation.AcceptAsync(
            new CommandEnvelope(
                McpInoCommandHandler.CommandType,
                2,
                commandId,
                commandContext,
                JsonSerializer.SerializeToElement(new { prompt }))).ConfigureAwait(false);
        return new
        {
            commandId,
            operationId = receipt.OperationId,
            phase = receipt.Phase.ToString(),
            receipt.Version
        };
    }

}
