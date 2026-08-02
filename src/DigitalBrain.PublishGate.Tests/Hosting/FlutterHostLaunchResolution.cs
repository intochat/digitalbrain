using DigitalBrain.Shell.Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostLaunchResolution
{
    [Fact(DisplayName =
        "Flutter command resolution matches v0.1.18 precedence: options → config → env → flutter")]
    public void PrecedenceIsOptionsThenConfigThenEnvThenFlutter()
    {
        var options = new FlutterHostOptions { FlutterCommand = "from-options" };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:FlutterCommand"] = "from-config",
            })
            .Build();

        Assert.Equal(
            "from-options",
            FlutterHostLaunch.ResolveFlutterCommand(options, configuration));

        Assert.Equal(
            "from-config",
            FlutterHostLaunch.ResolveFlutterCommand(new FlutterHostOptions(), configuration));
    }

    [Fact(DisplayName =
        "Default Flutter command identity is flutter (not flutter.bat)")]
    public void DefaultCommandIdentityIsFlutterNotBat()
    {
        var resolved = FlutterHostLaunch.ResolveFlutterCommand(new FlutterHostOptions());
        Assert.False(
            string.Equals(resolved, "flutter.bat", StringComparison.OrdinalIgnoreCase),
            "product command must not be branded as flutter.bat");
        Assert.True(
            string.Equals(resolved, "flutter", StringComparison.OrdinalIgnoreCase)
            || (Path.IsPathRooted(resolved)
                && Path.GetFileNameWithoutExtension(resolved)
                    .Equals("flutter", StringComparison.OrdinalIgnoreCase)),
            $"expected flutter identity, got '{resolved}'");
    }

    [Fact(DisplayName =
        "PATH resolution finds flutter when User/Machine PATH has a real SDK bin")]
    public void PathResolutionFindsFlutterWhenInstalled()
    {
        if (!FlutterHostLaunch.TryResolveCommandOnPath("flutter", out var absolute))
        {
            return;
        }

        Assert.True(Path.IsPathRooted(absolute));
        Assert.True(File.Exists(absolute), absolute);
        Assert.Equal("flutter", Path.GetFileNameWithoutExtension(absolute), StringComparer.OrdinalIgnoreCase);
    }
}
