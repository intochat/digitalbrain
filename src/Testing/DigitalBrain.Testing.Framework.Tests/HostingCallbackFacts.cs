using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;

namespace DigitalBrain.Tests;

public sealed class HostingCallbackFacts
{
    [Fact]
    public void ExplicitWebCallbackReplacesAutomaticWindowDefault()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DigitalBrain.slnx"))) { root = root.Parent; }
        Assert.NotNull(root);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [], DisableDashboard = true,
        });
        var brain = builder.AddDigitalBrain("test", persistentStorage: false);
        brain.WithModule<FlutterModule>(module => module.WithOptions(new()
        {
            Hosting = new() { Kind = FlutterHostKind.Web, WorkingDirectory = Path.Combine(root.FullName, "src/Modules/Flutter/app/core") },
        }));
        builder.AddExecutable("runtime", "unused", ".").WithReference(brain);
        var browser = Assert.Single(builder.Resources, r => r.Annotations.OfType<BrainBrowserAnnotation>().Any());
        Assert.Equal("FlutterShell", browser.Name);
    }
}
