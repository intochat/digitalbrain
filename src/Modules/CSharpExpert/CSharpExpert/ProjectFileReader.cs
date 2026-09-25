namespace DigitalBrain.CSharpExpert;

internal static class ProjectFileReader
{
    public static IReadOnlyList<string> TargetFrameworks(IEnumerable<string> projectPaths)
    {
        var frameworks = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in projectPaths)
        {
            foreach (var framework in ReadFrameworks(path))
            {
                frameworks.Add(framework);
            }
        }

        return [.. frameworks];
    }

    private static IEnumerable<string> ReadFrameworks(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return [];
        }

        try
        {
            var document = System.Xml.Linq.XDocument.Load(projectPath);
            return document.Descendants()
                .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (Exception error) when (error is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
