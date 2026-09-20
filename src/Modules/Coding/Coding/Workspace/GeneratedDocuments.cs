namespace DigitalBrain.Coding;

internal static class GeneratedDocuments
{
    private static readonly string[] Suffixes = [".g.cs", ".g.i.cs", ".generated.cs", ".designer.cs"];

    public static bool IsGenerated(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (Array.Exists(segments, static segment => string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fileName = segments.Length > 0 ? segments[^1] : path;
        return Array.Exists(Suffixes, suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
