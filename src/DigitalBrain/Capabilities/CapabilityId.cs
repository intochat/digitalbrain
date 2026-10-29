using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DigitalBrain.Capabilities;

/// <summary>
/// A versioned capability, written <c>name@major</c> (grammar <c>^[a-z][a-z0-9.-]{0,63}@[1-9][0-9]*$</c>).
/// Serialised to JSON as its string form.
/// </summary>
[JsonConverter(typeof(CapabilityIdJsonConverter))]
public readonly partial record struct CapabilityId
{
    /// <exception cref="ArgumentException"><paramref name="name"/> does not match <c>^[a-z][a-z0-9.-]{0,63}$</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="major"/> is less than 1.</exception>
    public CapabilityId(string name, int major)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 1);
        if (name is null || !NameRule().IsMatch(name))
        {
            throw new ArgumentException("A capability name is 1-64 characters: a lowercase letter, then lowercase letters, digits, '.' or '-'.", nameof(name));
        }
        Name = name;
        Major = major;
    }

    /// <summary>The capability name.</summary>
    public string Name { get; }

    /// <summary>The major version.</summary>
    public int Major { get; }

    /// <summary>True when this capability can stand in for <paramref name="required"/>: same name, same major.</summary>
    public bool Satisfies(CapabilityId required)
        => string.Equals(Name, required.Name, StringComparison.Ordinal) && Major == required.Major;

    /// <summary>Parses <c>name@major</c>.</summary>
    /// <exception cref="FormatException">The text is not a capability id.</exception>
    public static CapabilityId Parse(string text)
        => TryParse(text, out var id) ? id : throw new FormatException($"'{text}' is not a capability id (name@major, e.g. storage@1).");

    /// <summary>Parses <c>name@major</c>.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out CapabilityId id)
    {
        id = default;
        if (text is null) { return false; }
        var match = Grammar().Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return false;
        }
        id = new CapabilityId(match.Groups["name"].Value, major);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Name is null ? string.Empty : $"{Name}@{Major.ToString(CultureInfo.InvariantCulture)}";

    [GeneratedRegex("^(?<name>[a-z][a-z0-9.-]{0,63})@(?<major>[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex Grammar();

    [GeneratedRegex("^[a-z][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRule();
}

/// <summary>Reads and writes a <see cref="CapabilityId"/> as its <c>name@major</c> string.</summary>
public sealed class CapabilityIdJsonConverter : JsonConverter<CapabilityId>
{
    /// <inheritdoc />
    public override CapabilityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return CapabilityId.TryParse(text, out var id)
            ? id
            : throw new JsonException($"'{text}' is not a capability id (name@major, e.g. storage@1).");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CapabilityId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
