using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class PackableSurfaceContracts
{
    private const string TestingDescription =
        "Development-only DigitalBrain testing: assembly-owned real multi-silo fixtures, method-scoped TestBrain, deterministic time, typed journal evidence, closed durability faults, and exclusive Aspire AppHost testing.";

    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    public static TheoryData<string> PackableAssemblyNames { get; } = new(PackableProjects.Names);

    [Theory]
    [MemberData(nameof(PackableAssemblyNames))]
    public void EveryPublicConcreteReferenceTypeIsSealed(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var unsealedPublicTypes = assembly.GetExportedTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.IsSealed)
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(unsealedPublicTypes);
    }

    [Theory]
    [InlineData("DigitalBrain.Kernel")]
    [InlineData("DigitalBrain.Testing")]
    public async Task PackableTestingProductsCarryTheSourceGeneratorAnalyzer(
        string projectName)
    {
        var entries = await PackEntries(projectName);

        Assert.Contains(
            entries,
            entry => string.Equals(
                entry,
                "analyzers/dotnet/cs/DigitalBrain.SourceGeneration.dll",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TestingProjectOwnsOnlySurvivingDependencies()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "DigitalBrain.Testing",
            "DigitalBrain.Testing.csproj"));

        Assert.Contains(
            $"<Description>{TestingDescription}</Description>",
            project,
            StringComparison.Ordinal);
        Assert.Contains("<IsPackable>true</IsPackable>", project, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Microsoft.Orleans.Serialization.SystemTextJson",
            project,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PackageReference Include=\"Reqnroll\"",
            project,
            StringComparison.Ordinal);
    }

    private static async Task<string[]> PackEntries(string projectName)
    {
        var output = Path.Combine(
            Path.GetTempPath(),
            $"digitalbrain-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);

        try
        {
            var project = Path.Combine(
                RepositoryRoot,
                "src",
                projectName,
                projectName + ".csproj");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            process.StartInfo.ArgumentList.Add("pack");
            process.StartInfo.ArgumentList.Add(project);
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("Release");
            process.StartInfo.ArgumentList.Add("--no-build");
            process.StartInfo.ArgumentList.Add("--no-restore");
            process.StartInfo.ArgumentList.Add("--output");
            process.StartInfo.ArgumentList.Add(output);

            Assert.True(process.Start());
            var standardOutput = process.StandardOutput.ReadToEndAsync(
                TestContext.Current.CancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(
                TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(
                TestContext.Current.CancellationToken);
            var outputText = await standardOutput;
            var errorText = await standardError;

            Assert.True(
                process.ExitCode == 0,
                $"dotnet pack failed for {projectName}.{Environment.NewLine}{outputText}{Environment.NewLine}{errorText}");

            var package = Assert.Single(
                Directory.EnumerateFiles(
                    output,
                    "*.nupkg",
                    SearchOption.TopDirectoryOnly));
            await using var archive = await ZipFile.OpenReadAsync(
                package,
                TestContext.Current.CancellationToken);

            return archive.Entries
                .Select(entry => entry.FullName)
                .ToArray();
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(
                directory.FullName,
                "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "DigitalBrain.slnx was not found above the test assembly.");
    }
}
