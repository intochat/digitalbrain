using Microsoft.Extensions.Configuration;

namespace IntoChat.Tests.Unit;

public sealed class IdentityHostOptionsFacts
{
    [Theory]
    [InlineData(null, "intochat.session")]
    [InlineData("deployment.session", "deployment.session")]
    public void DeploymentConfigurationOverridesThePackagedHostDefaults(string? cookieOverride, string expectedCookie)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        if (cookieOverride is not null)
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalBrain:Identity:CookieName"] = cookieOverride });
        }
        Assert.Equal(expectedCookie, configuration["DigitalBrain:Identity:CookieName"]);
        Assert.Equal("IntoChat.v1", configuration["DigitalBrain:Identity:ProtectionApplicationName"]);
        Assert.Equal(IntoChatConfiguration.ProtectionContainerName, configuration["DigitalBrain:Identity:ProtectionContainerName"]);
    }
}
