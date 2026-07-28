namespace DigitalBrain.Tests;

internal static class RepositoryAssets
{
    private const string SolutionFileName = "DigitalBrain.slnx";

    private static readonly string Root = LocateRoot();

    internal static string Path(params string[] segments)
        => System.IO.Path.Combine([Root, .. segments]);

    private static string LocateRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"{SolutionFileName} was not found above the test assembly.");
    }
}
