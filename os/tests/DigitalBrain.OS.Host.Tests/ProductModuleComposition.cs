using Xunit;

namespace DigitalBrain.HostTests;

// TimeModule is Built and tested (src/modules/time) but was never composed into the product.
// This gate pins that the AppHost selects it and the silo carries the project reference to serve it.
public sealed class ProductModuleComposition
{
    [Fact(DisplayName = "Product AppHost selects TimeModule alongside the rest of the product module list")]
    public void ProductAppHostComposesTimeModule()
    {
        var root = FindRepositoryRoot();
        var appHost = File.ReadAllText(Path.Combine(root, "os", "DigitalBrain.OS.AppHost", "AppHost.cs"));

        Assert.Contains("using DigitalBrain.Time;", appHost, StringComparison.Ordinal);
        Assert.Contains("brain.AddModule<TimeModule>();", appHost, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Product AppHost and silo project reference DigitalBrain.Modules.Time, mirroring TasksModule")]
    public void ProductAppHostAndSiloReferenceTimeModule()
    {
        var root = FindRepositoryRoot();
        var appHostProject = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.AppHost", "DigitalBrain.OS.AppHost.csproj"));
        var siloProject = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.Host", "DigitalBrain.OS.Host.csproj"));

        const string timeProjectReference =
            @"src\modules\time\DigitalBrain.Modules.Time\DigitalBrain.Modules.Time.csproj";

        Assert.Contains(timeProjectReference, appHostProject, StringComparison.Ordinal);
        Assert.Contains(timeProjectReference, siloProject, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }
}
