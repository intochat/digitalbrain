using DigitalBrain.Google.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Google.Tests.Auth;

public sealed class GoogleOAuthOptionsTests
{
    [Theory(DisplayName = "Google OAuth options reject known placeholder credentials")]
    [InlineData("ClientId", "local-dev")]
    [InlineData("ClientSecret", "local-dev-secret")]
    [InlineData("RedirectUri", "http://localhost/oauth/callback")]
    public void Create_rejects_known_placeholders(string field, string placeholder)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{GoogleOAuthOptions.ConfigurationRoot}:ClientId"] =
                    field == "ClientId" ? placeholder : "real-client-id.apps.googleusercontent.com",
                [$"{GoogleOAuthOptions.ConfigurationRoot}:ClientSecret"] =
                    field == "ClientSecret" ? placeholder : "real-client-secret",
                [$"{GoogleOAuthOptions.ConfigurationRoot}:RedirectUri"] =
                    field == "RedirectUri" ? placeholder : "https://ui.example/oauth/callback",
            })
            .Build();

        var failure = Assert.Throws<InvalidOperationException>(() => GoogleOAuthOptions.Read(configuration));

        Assert.Contains("disallowed placeholder", failure.Message, StringComparison.Ordinal);
        Assert.Contains(GoogleOAuthOptions.ConfigurationRoot, failure.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Google OAuth options read ClientId ClientSecret RedirectUri under DigitalBrain:Google:Gmail")]
    public void Read_returns_projected_config_keys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{GoogleOAuthOptions.ConfigurationRoot}:ClientId"] = "client.apps.googleusercontent.com",
                [$"{GoogleOAuthOptions.ConfigurationRoot}:ClientSecret"] = "client-secret",
                [$"{GoogleOAuthOptions.ConfigurationRoot}:RedirectUri"] = "https://ui.example/oauth/callback",
            })
            .Build();

        var options = GoogleOAuthOptions.Read(configuration);

        Assert.Equal("client.apps.googleusercontent.com", options.ClientId);
        Assert.Equal("client-secret", options.ClientSecret);
        Assert.Equal(new Uri("https://ui.example/oauth/callback"), options.RedirectUri);
    }
}
