using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Boundary;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class AccountEnrichmentSampleContracts
{
    [Fact(DisplayName =
        "AccountEnrichment sample exposes IAccountEnrichment and EnrichmentModule capsule")]
    public void PublicContractAndCompiledModuleMatchQuickstartShape()
    {
        Assert.True(typeof(IAccountEnrichment).IsInterface);
        Assert.True(typeof(IAccountEnrichment).IsPublic);
        Assert.Contains(typeof(INeuron), typeof(IAccountEnrichment).GetInterfaces());

        ICompiledModule module = new EnrichmentModule();
        Assert.Equal(typeof(EnrichmentModule).FullName, module.Id.Value);
        Assert.Equal(EnrichmentModule.Id, module.Id);
    }

    [Fact(DisplayName =
        "AccountEnrichment process grain is internal and implements the public contract")]
    public void ProcessGrainIsInternalBehindThePublicContract()
    {
        var process = typeof(EnrichmentModule).Assembly
            .GetTypes()
            .Single(type =>
                type is { IsClass: true, IsPublic: false, IsNested: false }
                && type.Namespace == typeof(IAccountEnrichment).Namespace
                && typeof(IAccountEnrichment).IsAssignableFrom(type));

        Assert.Contains(typeof(IAccountEnrichment), process.GetInterfaces());
        Assert.Equal(
            NeuronId.GrainTypeNameOf(typeof(IAccountEnrichment)),
            NeuronId.GrainTypeNameOf(process));
    }

    [Fact(DisplayName =
        "Product silo does not reference AccountEnrichment without selecting EnrichmentModule")]
    public void ProductHostDoesNotCarryUnselectedSampleModule()
    {
        Assert.DoesNotContain(
            PackageBoundarySupport.CompileProjectsReachableFrom(PackageInventory.ProductSiloHost)
                .Append(PackageInventory.ProductSiloHost),
            project => project.Equals(PackageInventory.AccountEnrichment, StringComparison.Ordinal));
        Assert.DoesNotContain(
            PackageBoundarySupport.CompileProjectsReachableFrom(PackageInventory.ProductAppHost)
                .Append(PackageInventory.ProductAppHost),
            project => project.Equals(PackageInventory.AccountEnrichment, StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "Enrichment synapses keep stable wire aliases")]
    public void EnrichmentSynapseAliasesStayPinned()
    {
        Assert.Equal("db.account-enrichment.requested", WireAliasOf(typeof(EnrichAccountFromEmail)));
        Assert.Equal("db.account-enrichment.proposed", WireAliasOf(typeof(AccountEnrichmentProposed)));
        Assert.Equal("db.account-enrichment.completed", WireAliasOf(typeof(AccountEnriched)));
    }

    private static string WireAliasOf(Type type)
        => type.GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();
}
