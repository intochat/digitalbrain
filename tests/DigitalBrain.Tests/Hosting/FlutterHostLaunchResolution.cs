using DigitalBrain.Flutter.Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostLaunchResolution
{
    [Fact(DisplayName =
        "Flutter CLI resolution prefers absolute configured DigitalBrain:FlutterCommand over bare PATH name")]
    public void ConfigurationAbsolutePathWins()
    {
        var bat = Path.Combine(Path.GetTempPath(), $"flutter-{Guid.NewGuid():N}.bat");
        File.WriteAllText(bat, "@echo off");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DigitalBrain:FlutterCommand"] = bat,
                })
                .Build();

            var resolved = FlutterHostLaunch.ResolveFlutterCommand(
                new FlutterHostOptions(),
                configuration);

            Assert.Equal(Path.GetFullPath(bat), resolved, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(bat);
        }
    }

    [Fact(DisplayName =
        "Flutter CLI resolution fails loud when absolute configured path is missing")]
    public void MissingAbsoluteConfiguredPathThrows()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-flutter-{Guid.NewGuid():N}.bat");
        var options = new FlutterHostOptions
        {
            FlutterCommand = missing,
        };

        var failure = Assert.Throws<InvalidOperationException>(
            () => FlutterHostLaunch.ResolveFlutterCommand(options));

        Assert.Contains(missing, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not exist", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName =
        "Flutter CLI discovery finds an existing install under well-known or PATH roots")]
    public void DiscoveryFindsInstalledFlutterWhenPresent()
    {
        var known = @"E:\tools\flutter\bin\flutter.bat";
        if (!File.Exists(known))
        {
            return;
        }

        var resolved = FlutterHostLaunch.ResolveFlutterCommand(new FlutterHostOptions());
        Assert.True(File.Exists(resolved), $"resolved CLI missing: {resolved}");
        Assert.True(Path.IsPathRooted(resolved), $"expected absolute path, got: {resolved}");
        Assert.EndsWith("flutter.bat", resolved, StringComparison.OrdinalIgnoreCase);
    }
}
