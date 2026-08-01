using DigitalBrain.Mcp;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class McpOAuthPlaceholderRejection
{
    [Theory(DisplayName = "MCP OAuth options reject known placeholder credentials")]
    [InlineData("ClientId", "local-dev")]
    [InlineData("ClientSecret", "local-dev-secret")]
    [InlineData("RedirectUri", "http://localhost/oauth/callback")]
    public void CreateRejectsKnownPlaceholders(string field, string placeholder)
    {
        var server = new McpServerDefinition(
            "probe",
            "Probe Provider",
            new Uri("https://provider.example/mcp"),
            "DigitalBrain:Probe",
            ["probe.scope"]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{server.ConfigurationRoot}:ClientId"] = field == "ClientId" ? placeholder : "real-client-id",
                [$"{server.ConfigurationRoot}:ClientSecret"] = field == "ClientSecret" ? placeholder : "real-client-secret",
                [$"{server.ConfigurationRoot}:RedirectUri"] =
                    field == "RedirectUri" ? placeholder : "https://ui.example/oauth/mcp/callback",
            })
            .Build();

        var failure = Assert.Throws<InvalidOperationException>(
            () => McpOAuthOptions.Create(server, configuration, new EmptyTokenCache()));

        Assert.Contains("disallowed placeholder", failure.Message, StringComparison.Ordinal);
        Assert.Contains(server.DisplayName, failure.Message, StringComparison.Ordinal);
    }

    private sealed class EmptyTokenCache : ITokenCache
    {
        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<TokenContainer?>(null);
    }
}
