using DigitalBrain.Core;
using DigitalBrain.Modules.Sdk.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Google;

// Gmail is a configured MCP server, nothing more: the official Gmail MCP catalog is the
// capability surface, reached through the generic mcp gateway neuron (S1.6 strangler).
public sealed class GoogleModule : IModule
{
    public const string GmailServerKey = "google.gmail";
    public const string GmailDisplayName = "Gmail";
    public const string GmailConfigurationRoot = "DigitalBrain:Google:Gmail";

    // Official Developer Preview endpoint (Google Workspace MCP). Live OAuth and catalog
    // reachability require operator-provided Google client credentials.
    public static readonly Uri GmailMcpEndpoint = new("https://gmailmcp.googleapis.com/mcp/v1");

    public static readonly IReadOnlyList<string> GmailScopes =
    [
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.compose",
    ];

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(new McpServerDefinition(
            GmailServerKey,
            GmailDisplayName,
            GmailMcpEndpoint,
            GmailConfigurationRoot,
            GmailScopes,
            requiresClientSecret: true));
        builder.Services.AddSingleton(new ExternalServerCapability(GmailServerKey, GmailDisplayName));
    }
}
