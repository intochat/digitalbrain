using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

public static class JournalDirection
{
    public const string Incoming = "incoming";
    public const string Outgoing = "outgoing";

    public static string Validated(string? direction, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return Outgoing;
        }

        if (!Enum.TryParse<JournalKind>(direction.Trim(), ignoreCase: true, out var parsed))
        {
            throw new ArgumentException(
                $"Journal direction '{direction}' is not recognised. Use '{Incoming}' or '{Outgoing}'.",
                parameterName);
        }

        return Name(parsed);
    }

    public static JournalKind Parse(string direction)
        => Enum.Parse<JournalKind>(Validated(direction, nameof(direction)), ignoreCase: true);

    public static string Name(JournalKind kind)
        => kind == JournalKind.Incoming ? Incoming : Outgoing;
}
