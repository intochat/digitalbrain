namespace DigitalBrain.Microsoft.CSharp;

internal static class RepositoryRoot
{
    public static string? Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        return null;
    }
}
