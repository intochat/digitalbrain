using System.Text;

namespace DigitalBrain.Discovery.Search;

internal static class TextTokens
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "of", "to", "in", "on", "for", "and", "or", "with", "from", "by", "at",
        "is", "are", "was", "were", "be", "my", "me", "i", "we", "you", "it", "its", "this", "that",
        "can", "could", "would", "should", "how", "what", "when", "where", "which", "who", "please",
        "want", "need", "show", "give", "get", "make", "let", "into", "over", "all", "some", "any",
        "new", "about", "then", "than", "there", "here", "up", "out", "do", "does", "did",
    };

    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();
        var builder = new StringBuilder();
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                Flush(builder, tokens);
            }
        }

        Flush(builder, tokens);
        return tokens;
    }

    private static void Flush(StringBuilder builder, List<string> tokens)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var token = Stem(builder.ToString());
        builder.Clear();
        if (token.Length > 1 && !StopWords.Contains(token))
        {
            tokens.Add(token);
        }
    }

    private static string Stem(string token)
    {
        if (token.Length > 5 && token.EndsWith("ing", StringComparison.Ordinal))
        {
            return token[..^3];
        }

        if (token.Length > 4 && token.EndsWith("ed", StringComparison.Ordinal))
        {
            return token[..^2];
        }

        if (token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal))
        {
            return token[..^1];
        }

        return token;
    }
}
