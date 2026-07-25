using System.Reflection;
using DigitalBrain.Tests.Boundary;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class ResidualPackageGraphContracts
{
    [Fact(DisplayName =
        "Abstractions is Orleans.Sdk only — never Kernel, Client, Security, modules, or integrations")]
    public void AbstractionsGraphIsOrleansSdkOnly()
        => AssertResidualGraph(
            PackageInventory.Abstractions,
            directProjects: [],
            PackageInventory.AbstractionsDirectPackages,
            isForbiddenProject: PackageInventory.IsForbiddenOnConsumerResidual);

    [Fact(DisplayName =
        "Client graph is Abstractions + Orleans.Client only — never Kernel, Security, Mcp, or modules")]
    public void ClientGraphIsAbstractionsAndOrleansClientOnly()
        => AssertResidualGraph(
            PackageInventory.Client,
            PackageInventory.ClientDirectProjects,
            PackageInventory.ClientDirectPackages,
            isForbiddenProject: PackageInventory.IsForbiddenOnConsumerResidual);

    [Fact(DisplayName =
        "Security is configuration + DI abstractions only — no DigitalBrain projects or provider packages")]
    public void SecurityGraphIsConfigurationAndDiAbstractionsOnly()
    {
        AssertResidualGraph(
            PackageInventory.Security,
            directProjects: [],
            PackageInventory.SecurityDirectPackages);

        Assert.Empty(Assembly.Load(PackageInventory.Security).GetExportedTypes());
    }

    [Fact(DisplayName =
        "Integrations.Mcp is Security + southbound transport packages only — never Kernel, Client, or modules")]
    public void IntegrationsMcpGraphIsSecurityAndTransportOnly()
    {
        AssertResidualGraph(
            PackageInventory.IntegrationsMcp,
            PackageInventory.IntegrationsMcpDirectProjects,
            PackageInventory.IntegrationsMcpDirectPackages,
            isForbiddenProject: PackageInventory.IsForbiddenOnIntegrationsMcpProject);

        Assert.DoesNotContain(
            PackageBoundarySupport.DirectPackageReferencesOf(PackageInventory.IntegrationsMcp),
            PackageInventory.IsForbiddenOnIntegrationsMcpPackage);
        Assert.Empty(Assembly.Load(PackageInventory.IntegrationsMcp).GetExportedTypes());
    }

    [Fact(DisplayName =
        "Integrations.Mcp.Aspire.Hosting is Aspire.Hosting only — never Client, Kernel, modules, or southbound Mcp; no public exports")]
    public void IntegrationsMcpAspireHostingGraphIsAspireHostingOnly()
    {
        AssertResidualGraph(
            PackageInventory.IntegrationsMcpAspireHosting,
            PackageInventory.IntegrationsMcpAspireHostingDirectProjects,
            packages: null,
            compileReachable: PackageInventory.IntegrationsMcpAspireHostingCompileReachable,
            isForbiddenProject: PackageInventory.IsForbiddenOnAspireHostingProject);

        Assert.Empty(
            Assembly.Load(PackageInventory.IntegrationsMcpAspireHosting).GetExportedTypes());
    }

    [Fact(DisplayName =
        "metapackage is Abstractions + Client + Aspire only — never Kernel, Security, Mcp, Testing, Aspire.Hosting, or modules")]
    public void MetapackageGraphIsConsumerSurfaceOnly()
        => AssertResidualGraph(
            PackageInventory.Metapackage,
            PackageInventory.MetapackageDirectProjects,
            packages: null,
            isForbiddenProject: PackageInventory.IsForbiddenOnConsumerResidual);

    [Fact(DisplayName =
        "Aspire client integration is Client + Orleans.Client only — never Kernel, modules, or Aspire.Hosting")]
    public void AspireGraphIsClientSurfaceOnly()
    {
        AssertResidualGraph(
            PackageInventory.Aspire,
            PackageInventory.AspireDirectProjects,
            PackageInventory.AspireDirectPackages,
            compileReachable: PackageInventory.AspireCompileReachable,
            isForbiddenProject: PackageInventory.IsForbiddenOnConsumerResidual);

        Assert.Equal(
            ["DigitalBrainClientHostingExtensions"],
            Assembly.Load(PackageInventory.Aspire)
                .GetExportedTypes()
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName =
        "Aspire.Hosting is Abstractions + Aspire host packages only — never Client, Kernel, modules, or integrations")]
    public void AspireHostingGraphIsAbstractionsAndHostPackagesOnly()
    {
        AssertResidualGraph(
            PackageInventory.AspireHosting,
            PackageInventory.AspireHostingDirectProjects,
            PackageInventory.AspireHostingDirectPackages,
            isForbiddenProject: PackageInventory.IsForbiddenOnAspireHostingProject);

        Assert.Equal(
            [
                "ClientDigitalBrainReference",
                "DigitalBrainBuilder",
                "DigitalBrainHostingExtensions",
                "DigitalBrainModuleBuilder`1",
            ],
            Assembly.Load(PackageInventory.AspireHosting)
                .GetExportedTypes()
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName =
        "Testing graph is Client + Kernel + Integrations.Mcp only — never module runtimes or contracts")]
    public void TestingGraphIsClientKernelAndSouthboundMcpOnly()
        => AssertResidualGraph(
            PackageInventory.Testing,
            PackageInventory.TestingDirectProjects,
            PackageInventory.TestingDirectPackages,
            compileReachable: PackageInventory.TestingCompileReachable,
            isForbiddenProject: PackageInventory.IsForbiddenOnTestingProject);

    private static void AssertResidualGraph(
        string package,
        string[] directProjects,
        string[]? packages,
        string[]? compileReachable = null,
        Predicate<string>? isForbiddenProject = null)
    {
        Assert.Equal(
            directProjects,
            PackageBoundarySupport.DirectCompileProjectReferencesOf(package)
                .Order(StringComparer.Ordinal));

        if (packages is null)
        {
            Assert.Empty(PackageBoundarySupport.DirectPackageReferencesOf(package));
        }
        else
        {
            Assert.Equal(
                packages,
                PackageBoundarySupport.DirectPackageReferencesOf(package)
                    .Order(StringComparer.Ordinal));
        }

        var reachable = PackageBoundarySupport.CompileProjectsReachableFrom(package);
        Assert.Equal(
            compileReachable ?? directProjects,
            reachable.Order(StringComparer.Ordinal));

        if (isForbiddenProject is not null)
        {
            Assert.DoesNotContain(reachable, isForbiddenProject);
        }
    }
}
