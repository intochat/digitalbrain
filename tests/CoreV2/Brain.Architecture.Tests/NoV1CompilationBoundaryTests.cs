using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Brain.Architecture.Tests;

public sealed class NoV1CompilationBoundaryTests
{
    [Fact]
    public void Compiled_projects_never_reference_a_v1_project()
    {
        var references = ProjectReferenceScanner.ReadAll("DigitalBrain.slnx");
        var root = RepositoryRoot.Find();

        Assert.DoesNotContain(references, path => path.Contains("src/Kernel/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, path => path.Contains("src/Modules/", StringComparison.OrdinalIgnoreCase));
        Assert.All(references, project =>
        {
            var relativePath = Path.GetRelativePath(root, project).Replace('\\', '/');
            Assert.True(
                relativePath.StartsWith("src/CoreV2/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("tests/CoreV2/", StringComparison.OrdinalIgnoreCase),
                $"Compiled project '{relativePath}' is outside the CoreV2 source and test roots.");
        });
    }

    [Fact]
    public void Evaluated_graph_detects_an_imported_transitive_v1_project_reference()
    {
        using var fixture = ImportedTransitiveReferenceFixture.Create();

        var references = ProjectReferenceScanner.ReadAll(fixture.EntryProject);

        Assert.Contains(
            references,
            path => path.Contains("src/Kernel/Legacy/Legacy.csproj", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class ImportedTransitiveReferenceFixture : IDisposable
{
    private ImportedTransitiveReferenceFixture(string root, string entryProject)
    {
        Root = root;
        EntryProject = entryProject;
    }

    private string Root { get; }

    internal string EntryProject { get; }

    internal static ImportedTransitiveReferenceFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"brain-architecture-{Guid.NewGuid():N}");
        var entryProject = Path.Combine(root, "entry", "Entry.csproj");
        var bridgeProject = Path.Combine(root, "bridge", "Bridge.csproj");
        var legacyProject = Path.Combine(root, "src", "Kernel", "Legacy", "Legacy.csproj");

        Directory.CreateDirectory(Path.GetDirectoryName(entryProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(bridgeProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyProject)!);

        File.WriteAllText(
            Path.Combine(root, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup Condition="'$(MSBuildProjectName)' == 'Entry'">
                <ProjectReference Include="$(MSBuildThisFileDirectory)bridge/Bridge.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(entryProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            bridgeProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../src/Kernel/Legacy/Legacy.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(legacyProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        return new ImportedTransitiveReferenceFixture(root, entryProject);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal static class ProjectReferenceScanner
{
    internal static IReadOnlyList<string> ReadAll(string solutionPath)
    {
        var root = RepositoryRoot.Find();
        var entryPath = Path.GetFullPath(Path.IsPathRooted(solutionPath)
            ? solutionPath
            : Path.Combine(root, solutionPath));
        var graphPath = Path.Combine(Path.GetTempPath(), $"brain-project-graph-{Guid.NewGuid():N}.json");

        try
        {
            EvaluateGraph(root, entryPath, graphPath);
            using var graph = JsonDocument.Parse(File.ReadAllText(graphPath));

            return graph.RootElement
                .GetProperty("projects")
                .EnumerateObject()
                .Select(static project => project.Name.Replace('\\', '/'))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            File.Delete(graphPath);
        }
    }

    private static void EvaluateGraph(string workingDirectory, string entryPath, string graphPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(entryPath);
        startInfo.ArgumentList.Add("-target:GenerateRestoreGraphFile");
        startInfo.ArgumentList.Add($"-property:RestoreGraphOutputPath={graphPath}");
        startInfo.ArgumentList.Add("-nologo");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet msbuild to evaluate the project graph.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(milliseconds: 60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"MSBuild did not evaluate {entryPath} within 60 seconds.");
        }

        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"MSBuild project-graph evaluation failed with exit code {process.ExitCode}.{Environment.NewLine}" +
                output.Result + error.Result);
        }
    }
}

internal static class RepositoryRoot
{
    internal static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing DigitalBrain.slnx.");
    }
}
