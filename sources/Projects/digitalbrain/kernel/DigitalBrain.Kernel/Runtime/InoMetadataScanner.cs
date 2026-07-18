namespace DigitalBrain.Kernel.Runtime;

/// <summary>
/// Scans the front-matter of InoLang documents for price, licensing, and compatibility metadata.
/// </summary>
public sealed record InoBundleMetadata(
    string Price,
    string License,
    IReadOnlyList<string> Requires);

public static class InoMetadataScanner
{
    /// <summary>
    /// Extract @price, @license, and @requires metadata from an InoLang source file.
    /// Supports scanning from lines containing tags.
    /// </summary>
    public static InoBundleMetadata Scan(string source)
    {
        var price = "free";
        var license = "source-included";
        var requires = new List<string>();

        if (string.IsNullOrEmpty(source))
        {
            return new InoBundleMetadata(price, license, requires);
        }

        // We split by standard line breaks and scan each line
        var lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // We support comments containing tags as well (e.g. "// @price: free" or "# @price: free")
            var cleanLine = trimmed;
            if (cleanLine.StartsWith("//"))
            {
                cleanLine = cleanLine[2..].Trim();
            }
            else if (cleanLine.StartsWith("#"))
            {
                cleanLine = cleanLine[1..].Trim();
            }

            if (cleanLine.StartsWith("@price:", StringComparison.OrdinalIgnoreCase))
            {
                price = cleanLine["@price:".Length..].Trim();
            }
            else if (cleanLine.StartsWith("@license:", StringComparison.OrdinalIgnoreCase))
            {
                license = cleanLine["@license:".Length..].Trim();
            }
            else if (cleanLine.StartsWith("@requires:", StringComparison.OrdinalIgnoreCase))
            {
                requires.Add(cleanLine["@requires:".Length..].Trim());
            }
        }

        return new InoBundleMetadata(price, license, requires);
    }
}
