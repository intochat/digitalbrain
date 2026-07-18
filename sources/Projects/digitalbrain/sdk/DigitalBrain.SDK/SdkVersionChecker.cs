namespace DigitalBrain.SDK;

public static class SdkVersionChecker
{
    public const string CurrentSdkVersion = "5.1.0";

    public static bool IsSatisfied(string requiredName, string op, string requiredVersion)
    {
        if (!string.Equals(requiredName, "sdk", StringComparison.OrdinalIgnoreCase))
        {
            // Only 'sdk' capability is load-bearing in standard v5 metadata check
            return true;
        }

        if (!Version.TryParse(CurrentSdkVersion, out var currentVer))
        {
            return false;
        }

        if (!Version.TryParse(requiredVersion, out var reqVer))
        {
            // Try formatting version with fallback e.g. "5" -> "5.0.0"
            if (int.TryParse(requiredVersion, out var major))
            {
                reqVer = new Version(major, 0, 0);
            }
            else
            {
                return false;
            }
        }

        var cmp = currentVer.CompareTo(reqVer);
        return op switch
        {
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            "==" or "=" => cmp == 0,
            _ => cmp >= 0 // Default operator fallback is >=
        };
    }
}
