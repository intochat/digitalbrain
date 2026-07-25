namespace DigitalBrain.Tests.Boundary;

internal static class RepositoryLayout
{
    internal const string SolutionFileName = "DigitalBrain.slnx";
    internal const string ProjectExtension = ".csproj";

    internal const string Src = "src";
    internal const string Modules = "modules";
    internal const string Samples = "samples";
    internal const string Hosts = "hosts";

    private const string Bin = "bin";
    private const string Obj = "obj";
    private const string Worktrees = ".worktrees";
    private const string NodeModules = "node_modules";

    internal static readonly string Root = LocateRoot();

    internal static readonly string[] PackableTreeRoots = [Src, Modules, Samples];

    internal static readonly string[] ProjectTreeRoots = [Src, Modules, Hosts, Samples];

    private static readonly string[] IgnoredDirectoryNames =
        [Bin, Obj, Worktrees, NodeModules];

    internal static string ProjectFileName(string packageId) =>
        packageId + ProjectExtension;

    internal static bool IsIgnoredLookupPath(string file)
    {
        var relative = Path.GetRelativePath(Root, file);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            IgnoredDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static string LocateRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"{SolutionFileName} was not found above the test assembly.");
    }
}
