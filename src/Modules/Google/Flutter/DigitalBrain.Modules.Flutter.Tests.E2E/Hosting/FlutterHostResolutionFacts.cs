using DigitalBrain.Flutter.Aspire.Hosting;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Hosting;

public sealed class FlutterHostResolutionFacts
{
    [Fact]
    public void FlutterPackageResolvesFromAnyDepthUnderTheRepository()
    {
        var fromTestOutput = ShellHostingExtensions.ResolveFlutterWorkingDirectory(AppContext.BaseDirectory, configured: null);
        Assert.True(File.Exists(Path.Combine(fromTestOutput, "pubspec.yaml")), $"No Flutter package at '{fromTestOutput}'.");

        var deeper = Path.Combine(AppContext.BaseDirectory, "nested", "deeper", "still");
        Assert.Equal(fromTestOutput, ShellHostingExtensions.ResolveFlutterWorkingDirectory(deeper, configured: null));
    }

    [Fact]
    public void AnExplicitWorkingDirectoryWins()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "flutter-package"));
        Assert.Equal(absolute, ShellHostingExtensions.ResolveFlutterWorkingDirectory(AppContext.BaseDirectory, absolute));
        Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "relative")),
            ShellHostingExtensions.ResolveFlutterWorkingDirectory(AppContext.BaseDirectory, "relative"));
    }
}