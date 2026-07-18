using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.PackageTests;

[Collection(nameof(PackedFrameworkCollection))]
public sealed class PackageContentTests(PackedFrameworkFixture fixture)
{
    private static readonly string[] AllowedDependencyPrefixes =
    [
        "DigitalBrain.",
        "Microsoft.Orleans.",
        "Microsoft.Extensions."
    ];

    private static readonly string[] KernelExternalDependencies =
    [
        "Anthropic",
        "Aspire.Azure.Data.Tables",
        "Aspire.Azure.Storage.Blobs",
        "Aspire.Azure.Storage.Queues",
        "OpenAI"
    ];

    private static readonly string[] HostingDependencies =
    [
        "Aspire.Hosting",
        "Aspire.Hosting.Azure.Storage",
        "Aspire.Hosting.OpenAI",
        "Aspire.Hosting.Orleans"
    ];

    private static readonly string[] DevToolMarkers =
        ["Agents", "DevTools", "DevUI", "Dashboard"];

    private static readonly string[] ProviderAndJournalMarkers = ["OpenAI", "Anthropic", "Journaling"];

    private static readonly IReadOnlyDictionary<string, string[]> ApplicationFacingDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["DigitalBrain.Abstractions"] =
            [
                "Microsoft.Orleans.Core.Abstractions",
                "Microsoft.Orleans.Sdk"
            ],
            ["DigitalBrain.Aspire"] =
            [
                "Aspire.Azure.Data.Tables",
                "DigitalBrain.Client",
                "Microsoft.Extensions.Diagnostics.HealthChecks",
                "Microsoft.Orleans.Clustering.AzureStorage",
                "OpenTelemetry.Extensions.Hosting"
            ],
            ["DigitalBrain.Client"] =
            [
                "DigitalBrain.Abstractions",
                "Microsoft.Orleans.Client"
            ],
            ["DigitalBrain.DevTools"] =
            [
                "DigitalBrain.Aspire",
                "Microsoft.Agents.AI",
                "Microsoft.Agents.AI.DevUI",
                "Microsoft.Extensions.AI",
                "Microsoft.Orleans.Dashboard"
            ]
        };

    [Fact]
    public void Packable_projects_are_exactly_the_public_framework_packages()
    {
        Assert.Equal(
            [
                "DigitalBrain.Abstractions",
                "DigitalBrain.Aspire",
                "DigitalBrain.Aspire.Hosting",
                "DigitalBrain.Client",
                "DigitalBrain.DevTools",
                "DigitalBrain.Kernel"
            ],
            fixture.PackageIds);
    }

    [Fact]
    public void Framework_packages_and_symbol_packages_are_produced()
    {
        foreach (var packageId in fixture.PackageIds)
        {
            Assert.True(File.Exists(fixture.PackagePath(packageId)), $"{packageId} package is missing.");
            Assert.True(File.Exists(fixture.SymbolPackagePath(packageId)), $"{packageId} symbol package is missing.");
        }
    }

    [Fact]
    public void Package_metadata_is_complete()
    {
        foreach (var packageId in fixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.PackagePath(packageId));
            var nuspec = ReadNuspec(package, packageId);

            Assert.Equal(packageId, Element(nuspec, "id"));
            Assert.Equal(fixture.PackageVersion, Element(nuspec, "version"));
            Assert.False(string.IsNullOrWhiteSpace(Element(nuspec, "description")));
            Assert.Equal("Digital Brain Tech", Element(nuspec, "authors"));
            Assert.False(string.IsNullOrWhiteSpace(Element(nuspec, "tags")));
            Assert.Equal("https://github.com/digitalbraintech/brain", Element(nuspec, "projectUrl"));
            Assert.Equal("MIT", Element(nuspec, "license"));
            Assert.Equal("icon.png", Element(nuspec, "icon"));
            Assert.Equal("README.md", Element(nuspec, "readme"));
            Assert.NotNull(package.GetEntry("icon.png"));
            Assert.NotNull(package.GetEntry("README.md"));

            var repository = Descendant(nuspec, "repository");
            Assert.Equal("https://github.com/digitalbraintech/brain", repository.Attribute("url")?.Value);
            var commit = repository.Attribute("commit")?.Value;
            Assert.False(string.IsNullOrWhiteSpace(commit));
            Assert.Equal(40, commit!.Length);
        }
    }

    [Fact]
    public void Package_descriptions_are_package_specific()
    {
        var descriptions = fixture.PackageIds
            .Select(packageId =>
            {
                using var package = ZipFile.OpenRead(fixture.PackagePath(packageId));
                return Element(ReadNuspec(package, packageId), "description");
            })
            .ToArray();

        Assert.Equal(descriptions.Length, descriptions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Framework_packages_target_only_net8_with_xml_documentation()
    {
        foreach (var packageId in fixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.PackagePath(packageId));
            var libraryEntries = package.Entries
                .Where(entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal))
                .Select(entry => entry.FullName)
                .ToArray();

            Assert.NotEmpty(libraryEntries);
            Assert.All(libraryEntries, entry =>
                Assert.StartsWith("lib/net8.0/", entry, StringComparison.Ordinal));
            Assert.Contains($"lib/net8.0/{packageId}.dll", libraryEntries);
            Assert.Contains($"lib/net8.0/{packageId}.xml", libraryEntries);
        }
    }

    [Fact]
    public void Symbol_packages_contain_portable_pdbs()
    {
        foreach (var packageId in fixture.PackageIds)
        {
            using var symbolPackage = ZipFile.OpenRead(fixture.SymbolPackagePath(packageId));
            Assert.NotNull(symbolPackage.GetEntry($"lib/net8.0/{packageId}.pdb"));
        }
    }

    [Fact]
    public void Package_dependencies_stay_inside_the_public_graph()
    {
        foreach (var packageId in fixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.PackagePath(packageId));
            var dependencyIds = DependencyIds(ReadNuspec(package, packageId));

            Assert.All(dependencyIds, dependencyId =>
            {
                Assert.True(
                    ApplicationFacingDependencies.TryGetValue(packageId, out var exactDependencies)
                        ? exactDependencies.Contains(dependencyId, StringComparer.Ordinal)
                        : AllowedDependencyPrefixes.Any(prefix =>
                            dependencyId.StartsWith(prefix, StringComparison.Ordinal)) ||
                          packageId == "DigitalBrain.Aspire.Hosting" &&
                          HostingDependencies.Contains(dependencyId, StringComparer.Ordinal) ||
                          packageId == "DigitalBrain.Kernel" &&
                          KernelExternalDependencies.Contains(dependencyId, StringComparer.Ordinal),
                    $"{packageId} depends on unexpected package {dependencyId}.");
                if (packageId != "DigitalBrain.DevTools")
                {
                    Assert.All(DevToolMarkers, marker =>
                        Assert.DoesNotContain(
                            marker,
                            dependencyId,
                            StringComparison.OrdinalIgnoreCase));
                }
            });
        }
    }

    [Fact]
    public void Preview_development_dependencies_are_isolated_to_the_devtools_package()
    {
        foreach (var packageId in fixture.PackageIds)
        {
            using var package = ZipFile.OpenRead(fixture.PackagePath(packageId));
            var dependencyIds = DependencyIds(ReadNuspec(package, packageId));

            if (packageId == "DigitalBrain.DevTools")
            {
                Assert.Contains("Microsoft.Agents.AI.DevUI", dependencyIds);
                Assert.Contains("Microsoft.Orleans.Dashboard", dependencyIds);
                continue;
            }

            Assert.All(dependencyIds, dependencyId =>
                Assert.All(DevToolMarkers, marker =>
                    Assert.DoesNotContain(
                        marker,
                        dependencyId,
                        StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Devtools_dependency_versions_match_the_approved_preview_graph()
    {
        using var devToolsPackage = ZipFile.OpenRead(
            fixture.PackagePath("DigitalBrain.DevTools"));
        var devToolsNuspec = ReadNuspec(devToolsPackage, "DigitalBrain.DevTools");
        Assert.Equal(
            "1.13.0-preview.260703.1",
            DependencyVersion(devToolsNuspec, "Microsoft.Agents.AI.DevUI"));
        var dashboardVersion = DependencyVersion(
            devToolsNuspec,
            "Microsoft.Orleans.Dashboard");
        Assert.Equal("10.2.2-rc.2", dashboardVersion);

        using var clientPackage = ZipFile.OpenRead(
            fixture.PackagePath("DigitalBrain.Client"));
        var clientNuspec = ReadNuspec(clientPackage, "DigitalBrain.Client");
        Assert.Equal(
            dashboardVersion,
            DependencyVersion(clientNuspec, "Microsoft.Orleans.Client"));
    }

    [Fact]
    public void Application_facing_packages_have_no_provider_or_journal_dependency()
    {
        foreach (var (packageId, exactDependencies) in ApplicationFacingDependencies)
        {
            using var package = ZipFile.OpenRead(fixture.PackagePath(packageId));
            var dependencyIds = DependencyIds(ReadNuspec(package, packageId));

            Assert.Equal(
                exactDependencies.Order(StringComparer.Ordinal),
                dependencyIds.Order(StringComparer.Ordinal));
            Assert.All(dependencyIds, dependencyId =>
                Assert.All(ProviderAndJournalMarkers, marker =>
                    Assert.DoesNotContain(marker, dependencyId, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Client_package_depends_on_the_abstractions_package()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath("DigitalBrain.Client"));
        var dependencyIds = DependencyIds(ReadNuspec(package, "DigitalBrain.Client"));

        Assert.Contains("DigitalBrain.Abstractions", dependencyIds);
    }

    [Fact]
    public void Aspire_hosting_package_depends_only_on_abstractions_inside_the_public_graph()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath("DigitalBrain.Aspire.Hosting"));
        var dependencyIds = DependencyIds(ReadNuspec(package, "DigitalBrain.Aspire.Hosting"));

        Assert.Equal(
            ["DigitalBrain.Abstractions"],
            dependencyIds
                .Where(dependencyId =>
                    dependencyId.StartsWith("DigitalBrain.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Aspire_client_package_depends_only_on_the_client_inside_the_public_graph()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath("DigitalBrain.Aspire"));
        var dependencyIds = DependencyIds(ReadNuspec(package, "DigitalBrain.Aspire"));

        Assert.Equal(
            ["DigitalBrain.Client"],
            dependencyIds
                .Where(dependencyId =>
                    dependencyId.StartsWith("DigitalBrain.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Framework_projects_build_deterministically_with_repository_metadata()
    {
        foreach (var packageId in fixture.PackageIds)
        {
            var output = DotnetCli.RunChecked(
                fixture.RepositoryRoot,
                environment: null,
                "msbuild",
                fixture.ProjectPath(packageId),
                "-getProperty:Deterministic",
                "-getProperty:PublishRepositoryUrl",
                "-getProperty:EmbedUntrackedSources");
            using var properties = JsonDocument.Parse(output);
            var values = properties.RootElement.GetProperty("Properties");

            Assert.Equal("true", values.GetProperty("Deterministic").GetString());
            Assert.Equal("true", values.GetProperty("PublishRepositoryUrl").GetString());
            Assert.Equal("true", values.GetProperty("EmbedUntrackedSources").GetString());
        }
    }

    private static XDocument ReadNuspec(ZipArchive package, string packageId)
    {
        var entry = package.GetEntry($"{packageId}.nuspec")
            ?? throw new InvalidOperationException($"{packageId}.nuspec is missing from the package.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string? Element(XDocument nuspec, string localName) =>
        Descendant(nuspec, localName).Value;

    private static XElement Descendant(XDocument nuspec, string localName) =>
        nuspec.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)
            ?? throw new InvalidOperationException($"nuspec element {localName} is missing.");

    private static string[] DependencyIds(XDocument nuspec) =>
        nuspec.Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string? DependencyVersion(XDocument nuspec, string packageId) =>
        nuspec.Descendants()
            .Single(element =>
                element.Name.LocalName == "dependency" &&
                string.Equals(
                    element.Attribute("id")?.Value,
                    packageId,
                    StringComparison.Ordinal))
            .Attribute("version")
            ?.Value;
}
