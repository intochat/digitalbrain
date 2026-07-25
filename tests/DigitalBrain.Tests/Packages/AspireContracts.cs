using System.Reflection;
using DigitalBrain.Aspire;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Client;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class AspireContracts
{
    [Fact(DisplayName =
        "Aspire client package exports only DigitalBrainClientHostingExtensions — AddDigitalBrainClient surface")]
    public void AspireClientPublicExportsAreHostingExtensionsOnly()
    {
        var exports = typeof(DigitalBrainClientHostingExtensions).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(DigitalBrainClientHostingExtensions)], exports);
    }

    [Fact(DisplayName =
        "AddDigitalBrainClient is the only DI entry; owner defaults stay ambient config — not a second product door")]
    public void AspireClientSurfaceIsAddDigitalBrainClientAndOwnerAmbient()
    {
        var methods = typeof(DigitalBrainClientHostingExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(DigitalBrainClientHostingExtensions.AddDigitalBrainClient),
                nameof(DigitalBrainClientHostingExtensions.ResolveOwner),
            ],
            methods);

        Assert.Equal("dev", DigitalBrainClientHostingExtensions.DefaultOwner);
        Assert.Equal("DigitalBrain:Owner", DigitalBrainClientHostingExtensions.OwnerConfigurationKey);

        var addOverloads = typeof(DigitalBrainClientHostingExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(DigitalBrainClientHostingExtensions.AddDigitalBrainClient))
            .ToArray();
        Assert.Equal(2, addOverloads.Length);
        Assert.All(addOverloads, method => Assert.Equal(typeof(IHostApplicationBuilder), method.ReturnType));
    }

    [Fact(DisplayName =
        "Aspire.Hosting public exports are composition handle + projection API only — no projection guts")]
    public void AspireHostingPublicExportsAreCompositionSurfaceOnly()
    {
        var exports = typeof(DigitalBrainHostingExtensions).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(ClientDigitalBrainReference),
                nameof(DigitalBrainBuilder),
                nameof(DigitalBrainHostingExtensions),
                typeof(DigitalBrainModuleBuilder<>).Name,
            ],
            exports);

        Assert.DoesNotContain(
            typeof(DigitalBrainHostingExtensions).Assembly.GetExportedTypes(),
            type => type.Name is "DigitalBrainModuleProjection"
                or "AddBrain"
                or "StorageProfile"
                or "WithAzureStorage");
    }

    [Fact(DisplayName =
        "AddDigitalBrain is the single product brain entry — AddBrain / storage-profile APIs stay absent")]
    public void HostingProductEntryIsAddDigitalBrainOnly()
    {
        var methods = typeof(DigitalBrainHostingExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(DigitalBrainHostingExtensions.AddDigitalBrain),
                nameof(DigitalBrainHostingExtensions.AddModule),
                nameof(DigitalBrainHostingExtensions.WithReference),
            ],
            methods);

        Assert.DoesNotContain(
            typeof(DigitalBrainHostingExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance),
            method => method.Name is "AddBrain"
                or "WithAzureStorage"
                or "WithStorageProfile"
                or "AddAzureStorage");
    }

    [Fact(DisplayName =
        "DigitalBrainBuilder public author surface is AsClient only — Name, Journal, and guts stay non-public")]
    public void BuilderPublicSurfaceIsAsClientOnly()
    {
        var methods = typeof(DigitalBrainBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(DigitalBrainBuilder.AsClient)], methods);

        var publicProperties = typeof(DigitalBrainBuilder)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToArray();
        Assert.Empty(publicProperties);

        Assert.Null(
            typeof(DigitalBrainBuilder).GetProperty(
                "Name",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Null(
            typeof(DigitalBrainBuilder).GetProperty(
                "Journal",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Null(
            typeof(DigitalBrainBuilder).GetMethod(
                "SetJournal",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
    }

    [Fact(DisplayName =
        "ClientDigitalBrainReference and DigitalBrainModuleBuilder are typed tokens — no public members")]
    public void ProjectionTokensExposeNoPublicMembers()
    {
        Assert.DoesNotContain(
            typeof(ClientDigitalBrainReference)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
            member => member is not ConstructorInfo);

        Assert.DoesNotContain(
            typeof(DigitalBrainModuleBuilder<>)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
            member => member is not ConstructorInfo);
    }

    [Fact(DisplayName =
        "Hosting projection key constants stay on DigitalBrainHostingExtensions — silo journal name is journal")]
    public void HostingProjectionKeyConstantsAreStable()
    {
        Assert.Equal("journal", DigitalBrainHostingExtensions.JournalConnectionName);
        Assert.Equal(
            "DigitalBrain:Security:StateProtectionKey",
            DigitalBrainHostingExtensions.StateProtectionKeyConfigurationKey);
        Assert.Equal(
            "DigitalBrain:Modules",
            DigitalBrainHostingExtensions.ModulesConfigurationKey);
    }

    [Fact(DisplayName =
        "Aspire client DI returns host builder surface — never references Kernel, Aspire.Hosting, or modules")]
    public void AspireClientRegistersProgrammingModelNotHosting()
    {
        var returnTypes = typeof(DigitalBrainClientHostingExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(DigitalBrainClientHostingExtensions.AddDigitalBrainClient))
            .Select(method => method.ReturnType.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(IHostApplicationBuilder)], returnTypes);

        var referenced = typeof(DigitalBrainClientHostingExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(PackageInventory.AspireHosting, referenced);
        Assert.DoesNotContain(PackageInventory.Kernel, referenced);
        Assert.DoesNotContain(
            referenced,
            name => name.StartsWith(PackageInventory.ModulesPrefix, StringComparison.Ordinal));
        Assert.Contains(typeof(IDigitalBrain).Assembly.GetName().Name, referenced);
    }
}
