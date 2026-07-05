using DigitalBrain.Aspire;

namespace DigitalBrain.Tests.Aspire;

public sealed class ResolveDevFlutterAppPathTests
{
    [Fact]
    public void ResolveDevFlutterAppPath_ReturnsNull_WhenNoAppFolderNextToAppHost()
    {
        var tempAppHostDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempAppHostDir);
        try
        {
            var result = DigitalBrainBuilderExtensions.ResolveDevFlutterAppPath(tempAppHostDir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempAppHostDir, recursive: true);
        }
    }
}
