using DigitalBrain.Abstractions;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Auth;

// Shared MCP / northbound tool principal helper (was DigitalBrain.Mcp-internal).
public static class McpActor
{
    public static ActorContext Require(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        var http = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("MCP tools require an HTTP request context.");
        return HttpActor.Require(http);
    }

    public static string Partition(ActorContext actor, string localName)
        => PrincipalScoped.InstanceName(actor.PrincipalId, localName);

    public static PrincipalId ParsePrincipalId(string principalId, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId, paramName);
        if (!Guid.TryParse(principalId.Trim(), out var id) || id == Guid.Empty)
        {
            throw new ArgumentException(
                "Principal id must be a non-empty GUID (no spoof keys).",
                paramName);
        }

        return new PrincipalId(id);
    }
}
