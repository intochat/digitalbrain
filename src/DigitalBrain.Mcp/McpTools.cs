using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class McpAuthority(
    IHttpContextAccessor http,
    SessionTokenService tokens,
    IConfiguration configuration)
{
    public RuntimeRequestContext RequireContext()
    {
        var value = http.HttpContext?.Request.Headers.Authorization.ToString();
        _ = SessionAudiences.RequireFixedMcp(configuration["DigitalBrain:V2:Mcp:Audience"]);
        if (value is null || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            !tokens.TryValidate(value[7..].Trim(), SessionAudiences.Mcp, out var context))
            throw new UnauthorizedAccessException("Authenticated MCP session required.");
        return context;
    }
}

[McpServerToolType]
public sealed class McpReadTools(McpAuthority authority, IProjectionQueryPort projections)
{
    [McpServerTool(Name = "brain_read"), Description("Read the authenticated workspace-scoped timeline.")]
    public async Task<object> ReadAsync(CancellationToken cancellationToken = default)
    {
        var context = authority.RequireContext();
        return await projections.TimelineAsync(context, null, 50, cancellationToken);
    }
}

[McpServerToolType]
public sealed class McpMutationTools(McpAuthority authority, ApplicationService application)
{
    [McpServerTool(Name = "brain_act"), Description("Queue an authenticated, idempotent command.")]
    public async Task<object> ActAsync(
        string type,
        string commandId,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var context = authority.RequireContext();
        var command = new CommandEnvelope(type, 2, commandId, context, payload.Clone());
        return await application.SubmitAsync(context, command, cancellationToken);
    }

    [McpServerTool(Name = "brain_approve"), Description("Queue an authenticated approval command; requires brain.approve.")]
    public async Task<object> ApproveAsync(
        string commandId,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var context = authority.RequireContext();
        var command = new CommandEnvelope("approval", 2, commandId, context, payload.Clone());
        return await application.SubmitAsync(context, command, cancellationToken);
    }

    [McpServerTool(Name = "ino_interact"), Description("Queue an authenticated, idempotent INO interaction for the current workspace.")]
    public async Task<object> InoInteractAsync(
        string commandId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var context = authority.RequireContext();
        return await application.SubmitAsync(
            context,
            new CommandEnvelope(
                McpInoCommandHandler.CommandType,
                2,
                commandId,
                context,
                JsonSerializer.SerializeToElement(new { prompt })),
            cancellationToken);
    }
}

[McpServerToolType]
public sealed class McpAdminTools(McpAuthority authority, ApplicationService application)
{
    [McpServerTool(Name = "brain_admin"), Description("Queue an authenticated administrative command; requires brain.admin.")]
    public async Task<object> AdminAsync(
        string commandId,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var context = authority.RequireContext();
        var command = new CommandEnvelope("admin", 2, commandId, context, payload.Clone());
        return await application.SubmitAsync(context, command, cancellationToken);
    }
}
