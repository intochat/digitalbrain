using Xunit;

namespace DigitalBrain.PackageTests;

public sealed class PackedFrameworkFixture : IDisposable
{
    public PackedFrameworkFixture()
    {
        RepositoryRoot = LocateRepositoryRoot();
        PackableProjects = DiscoverPackableProjects(RepositoryRoot);
        if (PackableProjects.Count == 0)
            throw new InvalidOperationException("No packable DigitalBrain projects were discovered.");

        PackageVersion = DotnetCli.RunChecked(
            RepositoryRoot,
            environment: null,
            "msbuild",
            PackableProjects.Values.First(),
            "-getProperty:PackageVersion").Trim();

        FeedDirectory = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-packagetests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FeedDirectory);

        foreach (var projectPath in PackableProjects.Values)
            DotnetCli.RunChecked(
                RepositoryRoot,
                environment: null,
                "pack",
                projectPath,
                "-c",
                "Release",
                "-o",
                FeedDirectory,
                "-p:ContinuousIntegrationBuild=true",
                "--nologo");
    }

    public string RepositoryRoot { get; }

    public string FeedDirectory { get; }

    public string PackageVersion { get; }

    public IReadOnlyDictionary<string, string> PackableProjects { get; }

    public IReadOnlyList<string> PackageIds => PackableProjects.Keys.ToArray();

    public string ProjectPath(string packageId) => PackableProjects[packageId];

    public string PackagePath(string packageId) =>
        Path.Combine(FeedDirectory, $"{packageId}.{PackageVersion}.nupkg");

    public string SymbolPackagePath(string packageId) =>
        Path.Combine(FeedDirectory, $"{packageId}.{PackageVersion}.snupkg");

    public void Dispose()
    {
        try
        {
            Directory.Delete(FeedDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static SortedDictionary<string, string> DiscoverPackableProjects(string repositoryRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        var projects = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var searchRoot in new[] { "kernel", "integrations", "packages" })
        {
            var directory = Path.Combine(repositoryRoot, searchRoot);
            if (!Directory.Exists(directory))
                continue;

            foreach (var projectPath in Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories))
            {
                if (projectPath.Contains($"{separator}obj{separator}", StringComparison.Ordinal) ||
                    projectPath.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
                    continue;

                if (File.ReadAllText(projectPath).Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal))
                    projects.Add(Path.GetFileNameWithoutExtension(projectPath)!, projectPath);
            }
        }

        return projects;
    }

    private static string LocateRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}

[CollectionDefinition(nameof(PackedFrameworkCollection))]
public sealed class PackedFrameworkCollection : Xunit.ICollectionFixture<PackedFrameworkFixture>;
