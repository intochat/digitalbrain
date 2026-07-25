using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Quickstart;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class HostingPackageBoundaryContracts
{
    private const string McpHost = "DigitalBrain.Mcp";
    private const string ProductSiloHost = "DigitalBrain.Host";
    private const string QuickstartSiloHost = "DigitalBrain.Quickstart.Host";

    private static readonly string[] SiloAzureStoragePackages =
    [
        "Aspire.Azure.Data.Tables",
        "Microsoft.Orleans.Clustering.AzureStorage",
        "Microsoft.Orleans.Reminders.AzureStorage",
    ];

    private static readonly string[] McpDirectProjects =
    [
        PackageOf(typeof(DigitalBrainClientHostingExtensions)),
        PackageOf(typeof(DigitalBrainClient)),
        PackageOf(typeof(ILLM)),
    ];

    private static readonly string[] McpCompileReachable =
    [
        PackageOf(typeof(NeuronId)),
        PackageOf(typeof(DigitalBrainClientHostingExtensions)),
        PackageOf(typeof(DigitalBrainClient)),
        PackageOf(typeof(ILLM)),
        PackageOf(typeof(ITask)),
    ];

    private static readonly string[] UiDirectProjects =
    [
        PackageOf(typeof(DigitalBrainClientHostingExtensions)),
        PackageOf(typeof(DigitalBrainClient)),
        PackageOf(typeof(IShell)),
    ];

    private static readonly string[] UiCompileReachable =
    [
        PackageOf(typeof(NeuronId)),
        PackageOf(typeof(DigitalBrainClientHostingExtensions)),
        PackageOf(typeof(DigitalBrainClient)),
        PackageOf(typeof(IShell)),
    ];

    private static readonly string[] ProductSiloDirectProjects =
    [
        PackageOf(typeof(Neuron)),
        PackageOf(typeof(AIModule)),
        PackageOf(typeof(FlutterModule)),
        PackageOf(typeof(GoogleModule)),
        PackageOf(typeof(SalesforceModule)),
    ];

    private static readonly string[] ProductSiloCompileReachable =
    [
        PackageOf(typeof(NeuronId)),
        PackageInventory.IntegrationsMcp,
        PackageOf(typeof(Neuron)),
        PackageOf(typeof(AIModule)),
        PackageOf(typeof(ILLM)),
        PackageOf(typeof(FlutterModule)),
        PackageOf(typeof(IShell)),
        PackageOf(typeof(GoogleModule)),
        PackageOf(typeof(IGmail)),
        PackageOf(typeof(SalesforceModule)),
        PackageOf(typeof(ISalesforce)),
        PackageOf(typeof(ITask)),
        PackageInventory.Security,
    ];

    private static readonly string[] QuickstartSiloDirectProjects =
    [
        PackageOf(typeof(Neuron)),
        PackageOf(typeof(QuickstartModule)),
    ];

    private static readonly string[] QuickstartSiloCompileReachable =
    [
        PackageOf(typeof(NeuronId)),
        PackageOf(typeof(Neuron)),
        PackageOf(typeof(QuickstartModule)),
        PackageOf(typeof(IGreeter)),
    ];

    [Fact(DisplayName = "northbound MCP host is client + AI contracts only — never southbound providers")]
    public void NorthboundMcpHostCannotReachSouthboundProviders()
    {
        AssertGraph(
            McpHost,
            McpDirectProjects,
            McpCompileReachable,
            packageReferences: null);
    }

    [Fact(DisplayName = "northbound UI host is client + Flutter contracts only — never Kernel or southbound")]
    public void NorthboundUiHostCannotReachKernelOrSouthboundProviders()
    {
        AssertGraph(
            PackageInventory.Ui,
            UiDirectProjects,
            UiCompileReachable,
            packageReferences: null);
    }

    [Fact(DisplayName =
        "product silo host ships Kernel + available product module runtimes — never Ui, Client, or Aspire.Hosting")]
    public void ProductSiloHostShipsModuleRuntimesNotNorthboundEdges()
    {
        AssertGraph(
            ProductSiloHost,
            ProductSiloDirectProjects,
            ProductSiloCompileReachable,
            SiloAzureStoragePackages);
    }

    [Fact(DisplayName =
        "Quickstart silo host ships only Quickstart + Kernel — never product modules or northbound edges")]
    public void QuickstartSiloHostShipsOnlySampleCatalog()
    {
        AssertGraph(
            QuickstartSiloHost,
            QuickstartSiloDirectProjects,
            QuickstartSiloCompileReachable,
            SiloAzureStoragePackages);
    }

    private static void AssertGraph(
        string host,
        string[] directProjects,
        string[] compileReachable,
        string[]? packageReferences)
    {
        Assert.Equal(
            directProjects.Order(StringComparer.Ordinal),
            PackageBoundarySupport.DirectCompileProjectReferencesOf(host)
                .Order(StringComparer.Ordinal));

        if (packageReferences is not null)
        {
            Assert.Equal(
                packageReferences.Order(StringComparer.Ordinal),
                PackageBoundarySupport.DirectPackageReferencesOf(host)
                    .Order(StringComparer.Ordinal));
        }

        Assert.Equal(
            compileReachable.Order(StringComparer.Ordinal),
            PackageBoundarySupport.CompileProjectsReachableFrom(host)
                .Order(StringComparer.Ordinal));
    }

    private static string PackageOf(Type type)
        => type.Assembly.GetName().Name
           ?? throw new InvalidOperationException($"Assembly for {type.FullName} has no name.");
}
