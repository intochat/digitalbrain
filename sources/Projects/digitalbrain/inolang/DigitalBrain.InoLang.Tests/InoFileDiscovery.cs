namespace DigitalBrain.InoLang.Tests;

public static class InoFileDiscovery
{
    static readonly IReadOnlySet<string> ExcludedDirectories =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "Generated", ".git", "node_modules",
        };

    public static IReadOnlyList<string> Enumerate(string rootPath)
    {
        var found = new List<string>();
        Walk(new DirectoryInfo(rootPath), found);
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    static void Walk(DirectoryInfo dir, List<string> sink)
    {
        foreach (var file in dir.EnumerateFiles("*.ino"))
            sink.Add(file.FullName);

        foreach (var sub in dir.EnumerateDirectories())
            if (!ExcludedDirectories.Contains(sub.Name))
                Walk(sub, sink);
    }
}
