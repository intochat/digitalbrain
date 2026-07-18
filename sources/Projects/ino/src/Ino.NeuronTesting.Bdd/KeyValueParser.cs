using System.Text.RegularExpressions;

namespace Ino.NeuronTesting.Bdd;

public static class KeyValueParser
{
    // Matches: key="quoted value" or key=bareword (terminated by comma or whitespace).
    static readonly Regex _kvRegex = new(
        """(\w+)=(?:"([^"]*)"|([^,\s]+))""",
        RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string> Parse(string input)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in _kvRegex.Matches(input))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            dict[key] = value;
        }
        return dict;
    }
}
