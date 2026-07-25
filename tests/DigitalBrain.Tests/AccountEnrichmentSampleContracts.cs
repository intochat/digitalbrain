using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests;

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
        Assert.Equal(
            "DigitalBrain.AccountEnrichment.EnrichmentModule",
            module.Id.Value);
        Assert.Equal(EnrichmentModule.Id, module.Id);
    }

    [Fact(DisplayName =
        "AccountEnrichment process grain is internal and implements the public contract")]
    public void ProcessGrainIsInternalBehindThePublicContract()
    {
        var process = typeof(EnrichmentModule).Assembly
            .GetType(
                "DigitalBrain.AccountEnrichment.AccountEnrichment",
                throwOnError: true,
                ignoreCase: false)!;

        Assert.False(process.IsPublic);
        Assert.Contains(typeof(IAccountEnrichment), process.GetInterfaces());
        Assert.Equal(
            NeuronId.GrainTypeNameOf(typeof(IAccountEnrichment)),
            NeuronId.GrainTypeNameOf(process));
    }

    [Fact(DisplayName =
        "Product silo does not reference AccountEnrichment without selecting EnrichmentModule")]
    public void ProductHostDoesNotCarryUnselectedSampleModule()
    {
        var root = LocateRepositoryRoot();
        var hostCsproj = File.ReadAllText(Path.Combine(
            root,
            "hosts",
            "DigitalBrain.Host",
            "DigitalBrain.Host.csproj"));
        var appHost = File.ReadAllText(Path.Combine(
            root,
            "hosts",
            "DigitalBrain.AppHost",
            "AppHost.cs"));

        Assert.DoesNotContain(
            "DigitalBrain.AccountEnrichment",
            hostCsproj,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EnrichmentModule",
            appHost,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Enrichment synapses keep stable wire aliases")]
    public void EnrichmentSynapseAliasesStayPinned()
    {
        Assert.Contains(
            "db.account-enrichment.requested",
            WireAliasesOf(typeof(EnrichAccountFromEmail)));
        Assert.Contains(
            "db.account-enrichment.proposed",
            WireAliasesOf(typeof(AccountEnrichmentProposed)));
        Assert.Contains(
            "db.account-enrichment.completed",
            WireAliasesOf(typeof(AccountEnriched)));
    }

    private static IEnumerable<string> WireAliasesOf(Type type)
        => type.GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias);

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "DigitalBrain.slnx was not found above the test assembly.");
    }
}
