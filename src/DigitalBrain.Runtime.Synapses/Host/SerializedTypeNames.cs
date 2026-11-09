namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// Reads the type names out of a serialized (assembly-qualified) type name as attribute blobs store typeof arguments:
/// <c>Ns.Outer+Inner`1[[Arg, Asm]][], Asm, Version=...</c>. Every named type is returned, generic arguments included.
/// </summary>
internal static class SerializedTypeNames
{
    /// <summary>Each type the name refers to.</summary>
    /// <param name="serialized">The serialized name.</param>
    /// <returns>The full name (nested types joined by '.', arity dropped), the outermost type's full name, and the assembly or null.</returns>
    public static IReadOnlyList<(string Name, string Outer, string? Assembly)> Parse(string serialized)
    {
        ArgumentNullException.ThrowIfNull(serialized);
        var names = new List<(string, string, string?)>();
        var at = 0;
        Read(serialized, ref at, names, nested: false);
        return names;
    }

    private static void Read(string s, ref int at, List<(string, string, string?)> names, bool nested)
    {
        var start = at;
        while (at < s.Length && s[at] is not ('[' or ']' or ',' or '*' or '&'))
        {
            at++;
        }

        var raw = s[start..at].Trim();
        var index = names.Count;
        names.Add(("", "", null));

        // Generic arguments: [[A, Asm],[B, Asm]] or [A,B].
        if (at + 1 < s.Length && s[at] == '[' && s[at + 1] is not (']' or ','))
        {
            at++;
            while (at < s.Length && s[at] != ']')
            {
                if (s[at] == '[')
                {
                    at++;
                    Read(s, ref at, names, nested: true);
                    if (at < s.Length && s[at] == ']')
                    {
                        at++;
                    }
                }
                else
                {
                    Read(s, ref at, names, nested: true);
                }

                if (at < s.Length && s[at] == ',')
                {
                    at++;
                }
            }

            at++;
        }

        // Array, pointer and by-ref suffixes.
        while (at < s.Length && (s[at] is '*' or '&' || (s[at] == '[' && at + 1 < s.Length && s[at + 1] is ']' or ',')))
        {
            if (s[at] == '[')
            {
                while (at < s.Length && s[at] != ']')
                {
                    at++;
                }
            }

            at++;
        }

        string? assembly = null;
        if (at < s.Length && s[at] == ',')
        {
            // The assembly runs to the end, or to the bracket closing a generic argument.
            at++;
            var from = at;
            while (at < s.Length && (!nested || s[at] != ']'))
            {
                at++;
            }

            assembly = s[from..at].Split(',')[0].Trim();
        }

        var parts = raw.Split('+').Select(Plain).ToArray();
        names[index] = (string.Join('.', parts), parts[0], assembly);
    }

    private static string Plain(string part)
    {
        var tick = part.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? part : part[..tick];
    }
}
