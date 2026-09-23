using DigitalBrain.Contracts;
using IntoChat.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace IntoChat.Tests;

/// <summary>
/// P0.6 identity and class contract: capture is allowed only for the local owner's ordinary
/// development run, and an unknown host environment, identity, profile or class defaults off.
/// </summary>
public sealed class ContentCapturePolicyFacts
{
    [Theory]
    [InlineData("Development", null, null, true)]
    [InlineData("Development", "developer", "owner", true)]
    [InlineData("Production", null, null, false)]
    [InlineData("Production", "developer", "owner", false)]
    [InlineData("Development", "product", null, false)]
    [InlineData("Development", "product", "owner", false)]
    [InlineData("Development", "unknown", "owner", false)]
    [InlineData("Development", "developer", "intruder", false)]
    [InlineData("Staging", "developer", "owner", false)]
    public void LocalOwnerRequiresDevelopmentTheOwnerAndADeveloperProfile(
        string environmentName, string? profile, string? username, bool expected)
    {
        var auth = new BasicAuthOptions { Username = username };
        var values = profile is null ? [] : new Dictionary<string, string?> { [ContentCapturePolicy.ProfileKey] = profile };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Equal(expected, ContentCapturePolicy.IsLocalOwner(auth, configuration, new TestHostEnvironment(environmentName)));
    }

    [Theory]
    [InlineData(null, ContentClass.Ordinary)]
    [InlineData("ordinary", ContentClass.Ordinary)]
    [InlineData("Personal", ContentClass.Personal)]
    [InlineData("credential", ContentClass.Credential)]
    [InlineData("secret", ContentClass.Credential)]
    [InlineData("nonsense", ContentClass.Unknown)]
    public void ClassTagIsMinimalAndUnknownDefaultsOff(string? tag, ContentClass expected)
    {
        var messages = new[] { new AgentEndpoints.AgentMessage("user", "text", tag) };

        Assert.Equal(expected, ContentCapturePolicy.ClassOf(messages));
    }

    [Fact]
    public void TheHighestClassWinsAndAnUnknownMessageTurnsCaptureOff()
    {
        Assert.Equal(ContentClass.Personal, ContentCapturePolicy.ClassOf(
        [
            new AgentEndpoints.AgentMessage("user", "a", "ordinary"),
            new AgentEndpoints.AgentMessage("user", "b", "Personal"),
        ]));

        Assert.Equal(ContentClass.Unknown, ContentCapturePolicy.ClassOf(
        [
            new AgentEndpoints.AgentMessage("user", "a", "Personal"),
            new AgentEndpoints.AgentMessage("user", "b", "nonsense"),
        ]));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "IntoChat.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}