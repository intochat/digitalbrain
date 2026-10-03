using System.ComponentModel;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Platform.Identity.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class McpCaller(IHttpContextAccessor http, IOptions<AuthOptions> auth)
{
    [McpServerTool(Name = "neurons_context"), Description("Read the authenticated caller and selected brain. Use /brains/{brainId}/mcp to select a brain; /mcp uses the session/default brain.")]
    public CallerContext Context() => Stamp();

    // MCP dispatch can run in a separate execution context. Stamp from the current HTTP
    // request at the tool boundary, rather than inheriting a previous MCP session's caller.
    internal CallerContext Stamp()
    {
        var context = http.HttpContext ?? throw new UnauthorizedAccessException("MCP requires an HTTP caller.");
        var caller = AccountSession.ResolveCaller(context, auth.Value)
            ?? throw new UnauthorizedAccessException("MCP requires an authenticated caller.");
        return CallerContextStamper.Stamp(caller);
    }
}
