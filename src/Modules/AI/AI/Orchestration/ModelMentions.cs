namespace DigitalBrain.AI;

public static class ModelMentions
{
    public static IReadOnlyList<string> NamedIn(string text)
        => NamedIn(text, ModelContracts.KnownModelNames());

    internal static IReadOnlyList<string> NamedIn(string text, IReadOnlyList<string> known)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(known);

        return [.. known.Where(model => IsNamedIn(text, model, known))];
    }

    private static string FamilyOf(string model)
    {
        var letters = 0;

        while (letters < model.Length && char.IsAsciiLetter(model[letters]))
        {
            letters++;
        }

        return letters == 0 ? model : model[..letters];
    }

    private static bool IsNamedIn(string text, string model, IReadOnlyList<string> known)
    {
        var family = FamilyOf(model);
        var soleModelOfFamily = SoleModelOfFamily(known, family);

        for (var start = text.IndexOf(family, StringComparison.OrdinalIgnoreCase);
             start >= 0;
             start = text.IndexOf(family, start + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = start + family.Length;

            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            if ((start != 0 && char.IsLetterOrDigit(text[start - 1]))
                || (end != text.Length && char.IsLetterOrDigit(text[end])))
            {
                continue;
            }

            var named = text[start..end];
            var identifiesThisModel = named.Length == family.Length
                ? string.Equals(soleModelOfFamily, model, StringComparison.Ordinal)
                : string.Equals(named, model, StringComparison.OrdinalIgnoreCase);

            if (identifiesThisModel)
            {
                return true;
            }
        }

        return false;
    }

    private static string? SoleModelOfFamily(IReadOnlyList<string> known, string family)
    {
        string? sole = null;

        foreach (var candidate in known)
        {
            if (!string.Equals(FamilyOf(candidate), family, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sole is not null)
            {
                return null;
            }

            sole = candidate;
        }

        return sole;
    }
}
