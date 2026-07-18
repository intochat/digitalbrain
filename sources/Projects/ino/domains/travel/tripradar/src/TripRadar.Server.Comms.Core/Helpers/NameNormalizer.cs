namespace TripRadar.Server.Comms.Core.Helpers;

public static class NameNormalizer
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var position = 0;

        foreach (var character in value.Where(char.IsLetterOrDigit))
            buffer[position++] = char.ToUpperInvariant(character);

        return position == 0 ? string.Empty : new string(buffer[..position]);
    }
}
