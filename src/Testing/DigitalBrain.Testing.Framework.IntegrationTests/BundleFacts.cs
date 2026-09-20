using DigitalBrain.Google;

namespace DigitalBrain.Tests;

public sealed class BundleFacts
{
    [Fact]
    public void MissingBundleFailsBeforeInfrastructureStartup()
        => Assert.Throws<InvalidOperationException>(() => ModuleBundle.Validate(Path.GetTempPath(), [GoogleModule.Define(new())]));

    [Fact]
    public void BuiltBundleUsesRelativeDependencyEntry()
    {
        var bundle = ModuleBundle.Validate(AppContext.BaseDirectory, [GoogleModule.Define(new())]);
        Assert.Equal("DigitalBrain.Testing.Framework.IntegrationTests", bundle.Entry);
    }
}
