namespace DigitalBrain.InoLang.Tests;

public static class InoTestRunner
{
    static readonly ScenarioRunner Runner = new();

    public static async Task<InoFileReport> RunFileAsync(
        string absolutePath,
        string relativePath,
        IContractCatalog catalog,
        CancellationToken ct)
    {
        var source = await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false);
        var compiled = InoCompiler.Compile(source, catalog);

        if (!compiled.Success)
            return new InoFileReport(relativePath, compiled.Diagnostics, Scenarios: null);

        var report = await Runner.RunAllAsync(compiled.Plan!, ct).ConfigureAwait(false);
        return new InoFileReport(relativePath, compiled.Diagnostics, report);
    }

    public static async Task<DirectoryScenarioReport> RunDirectoryAsync(
        string rootPath,
        IContractCatalog catalog,
        CancellationToken ct)
    {
        var absoluteRoot = Path.GetFullPath(rootPath);
        var files = InoFileDiscovery.Enumerate(absoluteRoot);
        var results = new List<InoFileReport>(files.Count);
        foreach (var absolute in files)
        {
            var relative = Path.GetRelativePath(absoluteRoot, absolute);
            results.Add(await RunFileAsync(absolute, relative, catalog, ct).ConfigureAwait(false));
        }
        return new DirectoryScenarioReport(results);
    }
}
