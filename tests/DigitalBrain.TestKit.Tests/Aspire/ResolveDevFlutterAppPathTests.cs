using DigitalBrain.Aspire;
using Xunit;

namespace DigitalBrain.TestKit.Tests.Aspire;

public sealed class ResolveDevFlutterAppPathTests
{
    [Fact]
    public void ResolveDevFlutterAppPath_ReturnsRepoRootApp_WhenAppHostIsUnderHosts()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var appHostDir = Path.Combine(repoRoot, "hosts", "DigitalBrain.AppHost");
        var appDir = Path.Combine(repoRoot, "app");
        Directory.CreateDirectory(appHostDir);
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "pubspec.yaml"), "name: digital_brain_test");

        try
        {
            var result = FlutterAspireExtensions.ResolveDevFlutterAppPath(appHostDir);

            Assert.Equal(Path.GetFullPath(appDir), result);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }
}
