using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Core.V2;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Mcp;

[McpServerToolType]
public sealed class V2McpTools(
    IHttpContextAccessor http,
    V2SessionTokenService tokens,
    V2ApplicationService application,
    IV2ProjectionQueryPort projections)
{
    [McpServerTool(Name = "brain_read"), Description("Read the authenticated workspace-scoped V2 timeline.")]
    public async Task<object> ReadAsync(CancellationToken cancellationToken = default)
    {
        var context = RequireContext();
        return await projections.TimelineAsync(context, null, 50, cancellationToken);
    }

    [McpServerTool(Name = "brain_act"), Description("Queue an authenticated, idempotent V2 command.")]
    public async Task<object> ActAsync(string type, string commandId, JsonElement payload, CancellationToken cancellationToken = default)
    {
        var context = RequireContext();
        var command = new V2CommandEnvelope(type, 2, commandId, context, payload.Clone());
        return await application.SubmitAsync(context, command, cancellationToken);
    }

    [McpServerTool(Name = "brain_approve"), Description("Queue an authenticated approval command; requires brain.approve.")]
    public async Task<object> ApproveAsync(string commandId, JsonElement payload, CancellationToken cancellationToken = default)
    {
        var context = RequireContext();
        var command = new V2CommandEnvelope("approval", 2, commandId, context, payload.Clone());
        return await application.SubmitAsync(context, command, cancellationToken);
    }

    [McpServerTool(Name = "brain_admin"), Description("Queue an authenticated administrative command; requires brain.admin.")]
    public async Task<object> AdminAsync(string commandId, JsonElement payload, CancellationToken cancellationToken = default)
    {
        var context = RequireContext();
        var command = new V2CommandEnvelope("admin", 2, commandId, context, payload.Clone());
        return await application.SubmitAsync(context, command, cancellationToken);
    }

    private V2RequestContext RequireContext()
    {
        var request = http.HttpContext?.Request;
        var value = request?.Headers.Authorization.ToString();
        if (value is null || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !tokens.TryValidate(value[7..].Trim(), out var context))
            throw new UnauthorizedAccessException("Authenticated V2 session required.");
        return context;
    }
}
