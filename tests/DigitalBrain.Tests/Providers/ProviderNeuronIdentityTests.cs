using System.Reflection;
using DigitalBrain;
using DigitalBrain.Kernel;
using Google;
using Google.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;
using Salesforce;
using Salesforce.Contracts;
using Xunit;

namespace DigitalBrain.Tests.Providers;

public sealed class ProviderNeuronIdentityTests
{
    [Fact]
    public void Public_leaf_interfaces_declare_zero_methods_extend_INeuron_and_map_to_one_implementation()
    {
        AssertLeafIdentity(typeof(IGmail));
        AssertLeafIdentity(typeof(ISalesforce));

        var registrations = NeuronTypeCatalogBuilder.Build(
        [
            typeof(IGmail),
            typeof(GmailNeuron),
            typeof(ISalesforce),
            typeof(SalesforceNeuron),
            typeof(INeuron),
            typeof(Neuron),
        ]);

        Assert.Equal(2, registrations.Count);

        var gmail = Assert.Single(registrations, registration => registration.Contract == typeof(IGmail));
        Assert.Equal(typeof(GmailNeuron), gmail.Implementation);

        var salesforce = Assert.Single(registrations, registration => registration.Contract == typeof(ISalesforce));
        Assert.Equal(typeof(SalesforceNeuron), salesforce.Implementation);

        var quadrant = new DigitalBrain.Kernel.Quadrant();
        quadrant.Load(registrations);

        Assert.Equal(typeof(GmailNeuron), quadrant.GetImplementation<IGmail>());
        Assert.Equal(typeof(SalesforceNeuron), quadrant.GetImplementation<ISalesforce>());
        Assert.Equal(typeof(GmailNeuron), quadrant.Neurons[typeof(IGmail)]);
        Assert.Equal(typeof(SalesforceNeuron), quadrant.Neurons[typeof(ISalesforce)]);
    }

    [Fact]
    public async Task Get_binds_authenticated_owner_as_the_complete_grain_key_for_provider_leaves()
    {
        await using var cluster = await ProviderIdentityCluster.CreateAsync();
        var brain = new DigitalBrainClient(cluster.Client, new BrainOwnerId("owner-a"));

        var gmail = brain.Get<IGmail>();
        var salesforce = brain.Get<ISalesforce>();

        Assert.Equal("owner-a", gmail.GetPrimaryKeyString());
        Assert.Equal("owner-a", salesforce.GetPrimaryKeyString());
    }

    [Fact]
    public void Module_sources_reject_custom_provider_surface_beyond_empty_leaf_identities()
    {
        var moduleRoots = new[]
        {
            FindRepositoryDirectory("modules", "Google.Contracts"),
            FindRepositoryDirectory("modules", "Google"),
            FindRepositoryDirectory("modules", "Salesforce.Contracts"),
            FindRepositoryDirectory("modules", "Salesforce"),
        };

        var sourcePaths = moduleRoots
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedPath(path))
            .ToArray();
        var sources = sourcePaths.Select(File.ReadAllText).ToArray();
        var joined = string.Join('\n', sources);

        var projectSources = moduleRoots
            .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedPath(path))
            .Select(File.ReadAllText)
            .ToArray();
        var projects = string.Join('\n', projectSources);

        Assert.DoesNotContain("Ask", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("InvokeMcpTool", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("McpTool", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolName", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Google.Apis", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("DeveloperForce", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("ForceClient", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("record ", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Google.Apis", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("DeveloperForce", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelContextProtocol", projects, StringComparison.Ordinal);

        var contractInterfaces = typeof(IGmail).Assembly.GetExportedTypes()
            .Concat(typeof(ISalesforce).Assembly.GetExportedTypes())
            .Where(type => type.IsInterface)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([typeof(IGmail), typeof(ISalesforce)], contractInterfaces);

        foreach (var contract in contractInterfaces)
        {
            AssertLeafIdentity(contract);
            Assert.Empty(contract.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            Assert.Empty(contract.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }

        var concreteNeurons = typeof(GmailNeuron).Assembly.GetExportedTypes()
            .Concat(typeof(SalesforceNeuron).Assembly.GetExportedTypes())
            .Where(type => type.IsClass && !type.IsAbstract && typeof(Neuron).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([typeof(GmailNeuron), typeof(SalesforceNeuron)], concreteNeurons);

        Assert.Contains(typeof(GmailNeuron).Assembly.GetExportedTypes(), type => type == typeof(GoogleHosting));
        Assert.Contains(typeof(SalesforceNeuron).Assembly.GetExportedTypes(), type => type == typeof(SalesforceHosting));
        Assert.True(typeof(IGmail).IsAssignableFrom(typeof(GmailNeuron)));
        Assert.True(typeof(ISalesforce).IsAssignableFrom(typeof(SalesforceNeuron)));

        var exportedInterfacesBeyondLeaves = typeof(GmailNeuron).Assembly.GetExportedTypes()
            .Concat(typeof(SalesforceNeuron).Assembly.GetExportedTypes())
            .Where(type => type.IsInterface)
            .ToArray();
        Assert.Empty(exportedInterfacesBeyondLeaves);
    }

    private static void AssertLeafIdentity(Type leaf)
    {
        Assert.Contains(typeof(INeuron), leaf.GetInterfaces());
        Assert.Empty(leaf.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepositoryDirectory(params string[] relativeSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', relativeSegments)} from the test output directory.");
    }
}

file static class ProviderIdentityCluster
{
    public static async Task<TestCluster> CreateAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<ProviderIdentitySiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class ProviderIdentitySiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddBrainKernel();
            siloBuilder.AddGoogle();
            siloBuilder.AddSalesforce();
        }
    }
}
