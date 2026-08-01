using System.Text;
using DigitalBrain.Mcp;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class ProtectedResourceMetadataAlignment
{
    [Fact(DisplayName =
        "Google Workspace PRM resource /mcp is rewritten to /mcp/v1 so MCP OAuth resource match succeeds")]
    public void GoogleWorkspaceResourcePathIsAlignedToDocumentedEndpoint()
    {
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "authorization_servers": ["https://accounts.google.com/"],
              "bearer_methods_supported": ["header"],
              "resource": "https://gmailmcp.googleapis.com/mcp",
              "scopes_supported": ["https://www.googleapis.com/auth/gmail.readonly"]
            }
            """);

        Assert.True(McpProtectedResourceMetadataAlignmentHandler.TryAlign(payload, out var aligned));
        using var document = System.Text.Json.JsonDocument.Parse(aligned);
        Assert.Equal(
            "https://gmailmcp.googleapis.com/mcp/v1",
            document.RootElement.GetProperty("resource").GetString());
        Assert.Equal(
            "https://accounts.google.com",
            document.RootElement.GetProperty("authorization_servers")[0].GetString());
    }

    [Fact(DisplayName = "non-Google PRM payloads are left unchanged")]
    public void NonGoogleResourceIsUnchanged()
    {
        var payload = Encoding.UTF8.GetBytes(
            """
            {"resource":"https://api.example.com/mcp","authorization_servers":["https://auth.example.com/"]}
            """);

        Assert.False(McpProtectedResourceMetadataAlignmentHandler.TryAlign(payload, out _));
    }

    [Fact(DisplayName = "already-aligned Google /mcp/v1 resource is left unchanged")]
    public void AlreadyAlignedGoogleResourceIsUnchanged()
    {
        var payload = Encoding.UTF8.GetBytes(
            """
            {"resource":"https://gmailmcp.googleapis.com/mcp/v1","authorization_servers":["https://accounts.google.com/"]}
            """);

        Assert.False(McpProtectedResourceMetadataAlignmentHandler.TryAlign(payload, out _));
    }
}
