using System.Text;

namespace DigitalBrain.Coding;

// A unified-style diff of the lines that differ, computed from the common prefix and suffix; enough for a
// card and for the model to see what changed, without a full LCS.
internal static class LineDiff
{
    internal static string Render(string path, string before, string after)
    {
        var oldLines = before.Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
        var newLines = after.Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
            && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)])
        {
            suffix++;
        }

        var removed = oldLines.Length - prefix - suffix;
        var added = newLines.Length - prefix - suffix;
        if (removed == 0 && added == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder()
            .Append("--- a/").AppendLine(path)
            .Append("+++ b/").AppendLine(path)
            .Append("@@ -").Append(prefix + 1).Append(',').Append(removed).Append(" +").Append(prefix + 1).Append(',').Append(added).AppendLine(" @@");
        foreach (var line in oldLines.Skip(prefix).Take(removed))
        {
            text.Append('-').AppendLine(line);
        }

        foreach (var line in newLines.Skip(prefix).Take(added))
        {
            text.Append('+').AppendLine(line);
        }

        return text.ToString();
    }
}
