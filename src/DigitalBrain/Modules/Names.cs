using System.Text.RegularExpressions;

namespace DigitalBrain;

// The rule for names that later become store names, folder names and container names (T-03-12).
internal static partial class Names
{
    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex Safe();

    internal static bool IsSafe(string? name) => name is not null && Safe().IsMatch(name);

    internal static string Require(string? name, string parameter, string what)
    {
        if (!IsSafe(name))
        {
            throw new ArgumentException($"A {what} name is 1-63 characters: a lowercase letter, then lowercase letters, digits or '-'.", parameter);
        }
        return name!;
    }
}
