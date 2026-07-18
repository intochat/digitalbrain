using System.Text.RegularExpressions;

namespace DigitalBrain.Kernel.Creator;

public static partial class FeatureLlmTag
{
    [GeneratedRegex(@"@llm:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static bool TryRead(string featureText, out string model)
    {
        var match = Pattern().Match(featureText ?? string.Empty);
        model = match.Success ? match.Groups[1].Value : string.Empty;
        return match.Success;
    }
}
