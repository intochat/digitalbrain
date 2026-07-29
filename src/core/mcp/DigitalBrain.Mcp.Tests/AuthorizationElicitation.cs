using DigitalBrain.Abstractions;
using DigitalBrain.Mcp;
using ModelContextProtocol;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationElicitation
{
    [Fact(DisplayName =
        "AuthorizationRequired projects to URL-mode UrlElicitationRequiredException without secrets")]
    public void ProjectsToUrlModeElicitationWithoutSecrets()
    {
        var required = new AuthorizationRequired(
            CommandId.New(),
            "google.gmail",
            "DigitalBrain Gmail",
            new Uri("https://ui.test.digitalbrain.local/oauth/mcp/authorize?server=google.gmail&state=abc123"),
            "abc123");

        var elicitation = McpAuthorizationElicitation.For(required);

        Assert.IsType<UrlElicitationRequiredException>(elicitation);
        Assert.Single(elicitation.Elicitations);
        var request = elicitation.Elicitations[0];
        Assert.Equal("url", request.Mode);
        Assert.Equal(required.State, request.ElicitationId);
        Assert.Equal(required.SignInUrl.AbsoluteUri, request.Url);
        Assert.Contains("DigitalBrain Gmail", request.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("code=", elicitation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", elicitation.Message, StringComparison.OrdinalIgnoreCase);
    }
}
