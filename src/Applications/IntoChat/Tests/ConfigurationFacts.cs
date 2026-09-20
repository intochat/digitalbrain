using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using Microsoft.Extensions.Configuration;

namespace IntoChat.Tests;

public sealed class ConfigurationFacts
{
    [Fact]
    public void ExplicitDefaultsMatchApplicationBinding()
    {
        var bound = DigitalBrainConfiguration.Bind(new ConfigurationBuilder().Build());
        Assert.Equal(FlutterHostKind.Window, bound.Flutter.Hosting.Kind);
        Assert.Equal(ApplicationConfigurationTransport.Write(new DigitalBrainConfiguration()),
            ApplicationConfigurationTransport.Write(bound));
    }

    [Fact]
    public void DeepSettingsSurviveSnapshotAndApplicationBinding()
    {
        var original = new DigitalBrainConfiguration
        {
            Flutter = new()
            {
                Hosting = new() { Kind = FlutterHostKind.Web, ChatName = "scenario", ShellName = "visible" },
            },
            Google = new() { PublicOrigin = new("http://localhost:1234/"), TokenEndpoint = new("http://localhost:1234/token") },
        };
        var resolved = ApplicationConfigurationTransport.Read(
            ApplicationConfigurationTransport.Write(original), new DigitalBrainConfiguration().Modules);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(resolved.SelectMany(m => m.Configuration)).Build();
        var rebound = DigitalBrainConfiguration.Bind(configuration);
        Assert.Equal("scenario", rebound.Flutter.Hosting.ChatName);
        Assert.Equal("visible", rebound.Flutter.Hosting.ShellName);
        Assert.Equal(FlutterHostKind.Web, rebound.Flutter.Hosting.Kind);
        Assert.Equal("http://localhost:1234/token", rebound.Google.TokenEndpoint.AbsoluteUri);
    }
}
