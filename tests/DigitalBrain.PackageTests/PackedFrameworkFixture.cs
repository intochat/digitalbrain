using Xunit;

namespace DigitalBrain.PackageTests;

public sealed class PackedFrameworkFixture : IDisposable
{
    private readonly bool _ownsFeed;
    private readonly object _quickstartLock = new();
    private QuickstartBuild? _quickstart;

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

        var suppliedFeed = Environment.GetEnvironmentVariable(
            "DIGITALBRAIN_PACKAGE_FEED");
        if (string.IsNullOrWhiteSpace(suppliedFeed))
        {
            FeedDirectory = Path.Combine(
                Path.GetTempPath(),
                "digitalbrain-packagetests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(FeedDirectory);
            _ownsFeed = true;

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
        else
        {
            FeedDirectory = Path.GetFullPath(suppliedFeed, RepositoryRoot);
            if (!Directory.Exists(FeedDirectory))
                throw new DirectoryNotFoundException(
                    $"The supplied DigitalBrain package feed does not exist: {FeedDirectory}");
        }

        foreach (var packageId in PackageIds)
        {
            if (!File.Exists(PackagePath(packageId)))
                throw new InvalidOperationException(
                    $"{packageId} {PackageVersion} is missing from {FeedDirectory}.");
        }
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

    public QuickstartBuild PrepareQuickstart()
    {
        lock (_quickstartLock)
        {
            if (_quickstart is not null)
                return _quickstart;

            var sourceRoot = Path.Combine(
                RepositoryRoot,
                "samples",
                "DigitalBrain.Quickstart");
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException(
                    $"The package quickstart does not exist: {sourceRoot}");

            var workspace = Path.Combine(
                Path.GetTempPath(),
                "digitalbrain-quickstart-tests",
                Guid.NewGuid().ToString("N"));
            var quickstartRoot = Path.Combine(workspace, "DigitalBrain.Quickstart");
            CopyDirectory(sourceRoot, quickstartRoot);

            var packagesDirectory = Path.Combine(workspace, "packages");
            var httpCacheDirectory = Path.Combine(workspace, "http-cache");
            var pluginsCacheDirectory = Path.Combine(workspace, "plugins-cache");
            var cliHomeDirectory = Path.Combine(workspace, "cli-home");
            Directory.CreateDirectory(packagesDirectory);
            Directory.CreateDirectory(httpCacheDirectory);
            Directory.CreateDirectory(pluginsCacheDirectory);
            Directory.CreateDirectory(cliHomeDirectory);

            var nugetConfig = Path.Combine(workspace, "NuGet.config");
            File.WriteAllText(
                nugetConfig,
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="digitalbrain-local" value="{Xml(FeedDirectory)}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="digitalbrain-local">
                      <package pattern="DigitalBrain.*" />
                    </packageSource>
                    <packageSource key="nuget.org">
                      <package pattern="Aspire*" />
                      <package pattern="*" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);

            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NUGET_PACKAGES"] = packagesDirectory,
                ["NUGET_HTTP_CACHE_PATH"] = httpCacheDirectory,
                ["NUGET_PLUGINS_CACHE_PATH"] = pluginsCacheDirectory,
                ["DOTNET_CLI_HOME"] = cliHomeDirectory,
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                ["DOTNET_NOLOGO"] = "1"
            };
            var appHost = Path.Combine(
                quickstartRoot,
                "DigitalBrain.Quickstart.AppHost",
                "DigitalBrain.Quickstart.AppHost.csproj");
            try
            {
                DotnetCli.RunChecked(
                    quickstartRoot,
                    environment,
                    "restore",
                    appHost,
                    "--configfile",
                    nugetConfig,
                    "--no-cache",
                    "--force",
                    $"-p:DigitalBrainVersion={PackageVersion}",
                    "--nologo");
                DotnetCli.RunChecked(
                    quickstartRoot,
                    environment,
                    "build",
                    appHost,
                    "-c",
                    "Release",
                    "--no-restore",
                    $"-p:DigitalBrainVersion={PackageVersion}",
                    "--nologo");

                _quickstart = new QuickstartBuild(
                    quickstartRoot,
                    packagesDirectory,
                    environment);
                return _quickstart;
            }
            catch
            {
                DeleteDirectory(workspace);
                throw;
            }
        }
    }

    public void Dispose()
    {
        if (_quickstart is not null)
            DeleteDirectory(Directory.GetParent(_quickstart.Root)!.FullName);
        if (_ownsFeed)
            DeleteDirectory(FeedDirectory);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj")
                continue;
            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Xml(string value) =>
        System.Security.SecurityElement.Escape(value)
        ?? throw new InvalidOperationException("A NuGet source path could not be encoded.");

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

public sealed record QuickstartBuild(
    string Root,
    string PackagesDirectory,
    IReadOnlyDictionary<string, string> Environment)
{
    public string Assembly(string projectName, string targetFramework) =>
        Path.Combine(
            Root,
            projectName,
            "bin",
            "Release",
            targetFramework,
            $"{projectName}.dll");
}

[CollectionDefinition(nameof(PackedFrameworkCollection))]
public sealed class PackedFrameworkCollection : Xunit.ICollectionFixture<PackedFrameworkFixture>;
