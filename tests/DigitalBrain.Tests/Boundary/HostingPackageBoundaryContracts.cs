using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class HostingPackageBoundaryContracts
{
    [Fact]
    public void NorthboundMcpHostCannotReachSouthboundProviders()
    {
        Assert.Equal(
            ["DigitalBrain.Aspire", "DigitalBrain.Client", "DigitalBrain.Modules.AI.Contracts"],
            PackageBoundarySupport.DirectCompileProjectReferencesOf("DigitalBrain.Mcp")
                .Order(StringComparer.Ordinal));

        Assert.DoesNotContain(
            PackageBoundarySupport.CompileProjectsReachableFrom("DigitalBrain.Mcp"),
            project => project.StartsWith("DigitalBrain.Integrations.Mcp", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Google", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Salesforce", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "northbound UI host is client + Flutter contracts only — never Kernel or southbound")]
    public void NorthboundUiHostCannotReachKernelOrSouthboundProviders()
    {
        Assert.Equal(
            [
                "DigitalBrain.Aspire",
                "DigitalBrain.Client",
                "DigitalBrain.Modules.Flutter.Contracts",
            ],
            PackageBoundarySupport.DirectCompileProjectReferencesOf("DigitalBrain.Ui")
                .Order(StringComparer.Ordinal));

        var reachable = PackageBoundarySupport.CompileProjectsReachableFrom("DigitalBrain.Ui");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Kernel");
        Assert.DoesNotContain(
            reachable,
            project => project == "DigitalBrain.Modules.Flutter"
                || project.StartsWith("DigitalBrain.Integrations.Mcp", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Google", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Salesforce", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.AI", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "product silo host ships Kernel + available product module runtimes — never Ui, Client, or Aspire.Hosting")]
    public void ProductSiloHostShipsModuleRuntimesNotNorthboundEdges()
    {
        Assert.Equal(
            [
                "DigitalBrain.Kernel",
                "DigitalBrain.Modules.AI",
                "DigitalBrain.Modules.Flutter",
                "DigitalBrain.Modules.Google",
                "DigitalBrain.Modules.Salesforce",
            ],
            PackageBoundarySupport.DirectCompileProjectReferencesOf("DigitalBrain.Host")
                .Order(StringComparer.Ordinal));

        Assert.Equal(
            [
                "Aspire.Azure.Data.Tables",
                "Microsoft.Orleans.Clustering.AzureStorage",
                "Microsoft.Orleans.Reminders.AzureStorage",
            ],
            PackageBoundarySupport.DirectPackageReferencesOf("DigitalBrain.Host")
                .Order(StringComparer.Ordinal));

        var reachable = PackageBoundarySupport.CompileProjectsReachableFrom("DigitalBrain.Host");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Ui");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Client");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Mcp");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Testing");
        Assert.DoesNotContain(
            reachable,
            project => project.StartsWith("DigitalBrain.Aspire.Hosting", StringComparison.Ordinal)
                || project.EndsWith(".Aspire.Hosting", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "Quickstart silo host ships only Quickstart + Kernel — never product modules or northbound edges")]
    public void QuickstartSiloHostShipsOnlySampleCatalog()
    {
        Assert.Equal(
            [
                "DigitalBrain.Kernel",
                "DigitalBrain.Quickstart",
            ],
            PackageBoundarySupport.DirectCompileProjectReferencesOf("DigitalBrain.Quickstart.Host")
                .Order(StringComparer.Ordinal));

        Assert.Equal(
            [
                "Aspire.Azure.Data.Tables",
                "Microsoft.Orleans.Clustering.AzureStorage",
                "Microsoft.Orleans.Reminders.AzureStorage",
            ],
            PackageBoundarySupport.DirectPackageReferencesOf("DigitalBrain.Quickstart.Host")
                .Order(StringComparer.Ordinal));

        var reachable = PackageBoundarySupport.CompileProjectsReachableFrom("DigitalBrain.Quickstart.Host");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Ui");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Host");
        Assert.DoesNotContain(
            reachable,
            project => project.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "silo Program.cs is env-selected AddDigitalBrain only — no Ui hand-wire or module activation in source")]
    public void SiloProgramsAreHonestEnvSelectedActivation()
    {
        foreach (var relative in new[]
                 {
                     Path.Combine("hosts", "DigitalBrain.Host", "Program.cs"),
                     Path.Combine("hosts", "DigitalBrain.Quickstart.Host", "Program.cs"),
                 })
        {
            var program = File.ReadAllText(Path.Combine(PackageBoundarySupport.RepositoryRoot, relative));
            Assert.Contains("AddDigitalBrain()", program, StringComparison.Ordinal);
            Assert.Contains("AddDigitalBrainJournalStorage", program, StringComparison.Ordinal);
            Assert.Contains("MapGet(\"/health\"", program, StringComparison.Ordinal);
            Assert.DoesNotContain("MapUi", program, StringComparison.Ordinal);
            Assert.DoesNotContain("AddModule", program, StringComparison.Ordinal);
            Assert.DoesNotContain("WithUiEdge", program, StringComparison.Ordinal);
            Assert.DoesNotContain("WithFlutterHost", program, StringComparison.Ordinal);
            Assert.DoesNotContain("DigitalBrain.Ui", program, StringComparison.Ordinal);
            Assert.DoesNotContain("IGrainFactory", program, StringComparison.Ordinal);
            Assert.DoesNotContain("AddDigitalBrainClient", program, StringComparison.Ordinal);
        }
    }
}
