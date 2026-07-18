using Xunit;

namespace DigitalBrain.PackageTests;

[Collection(nameof(PackedFrameworkCollection))]
public sealed class EmptyFeedRestoreTests(PackedFrameworkFixture fixture)
{
    [Fact]
    public void Consumer_restores_and_builds_from_an_empty_cache_and_the_local_feed()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-packagetests",
            $"consumer-{Guid.NewGuid():N}");
        var consumerDirectory = Path.Combine(workspace, "consumer");
        var packagesDirectory = Path.Combine(workspace, "packages");
        Directory.CreateDirectory(consumerDirectory);
        Directory.CreateDirectory(packagesDirectory);

        try
        {
            var packageReferences = string.Join(
                Environment.NewLine,
                fixture.PackageIds.Select(packageId =>
                    $"""    <PackageReference Include="{packageId}" Version="{fixture.PackageVersion}" />"""));
            File.WriteAllText(
                Path.Combine(consumerDirectory, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                {packageReferences}
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(consumerDirectory, "NuGet.config"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="digitalbrain-local" value="{fixture.FeedDirectory}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            var output = DotnetCli.RunChecked(
                consumerDirectory,
                new Dictionary<string, string> { ["NUGET_PACKAGES"] = packagesDirectory },
                "build",
                "Consumer.csproj",
                "--nologo");

            Assert.Contains("Consumer", output, StringComparison.Ordinal);
            Assert.True(
                Directory.Exists(Path.Combine(packagesDirectory, "digitalbrain.client")),
                "DigitalBrain.Client was not restored from the local feed into the isolated cache.");
        }
        finally
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
