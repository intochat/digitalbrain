using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain;

/// <summary>A neuron's identity: its brain, its frozen type name and its key. Written <c>brain/type/key</c>.</summary>
/// <remarks>The key is opaque and may contain '/'; it is never a file path.</remarks>
[JsonConverter(typeof(NeuronIdJsonConverter))]
public readonly record struct NeuronId
{
    private const string Shape = "A neuron id has the shape brain/type/key with a valid brain id, a type without '/' and a non-empty key.";

    /// <exception cref="ArgumentException">The type or key is empty, or the type contains '/'.</exception>
    public NeuronId(BrainId brain, string type, string key)
    {
        if (brain.Value is null) { throw new ArgumentException("A neuron id needs a brain.", nameof(brain)); }
        if (!IsType(type)) { throw new ArgumentException("A neuron type is non-empty and has no '/' or control characters.", nameof(type)); }
        if (string.IsNullOrEmpty(key) || key.Any(char.IsControl)) { throw new ArgumentException("A neuron key is non-empty and has no control characters.", nameof(key)); }
        Brain = brain;
        Type = type;
        Key = key;
    }

    /// <summary>The brain the neuron lives in.</summary>
    public BrainId Brain { get; }

    /// <summary>The frozen neuron type name.</summary>
    public string Type { get; }

    /// <summary>The key, unique per type within the brain.</summary>
    public string Key { get; }

    /// <summary>Deconstructs into brain, type and key.</summary>
    public void Deconstruct(out BrainId brain, out string type, out string key)
    {
        brain = Brain;
        type = Type;
        key = Key;
    }

    /// <summary>Parses <c>brain/type/key</c>. System scopes do not parse.</summary>
    /// <exception cref="FormatException"><paramref name="text"/> does not have the shape.</exception>
    public static NeuronId Parse(string text)
        => TryParse(text, out var id) ? id : throw new FormatException(Shape);

    /// <summary>Parses <c>brain/type/key</c>; false when the text does not have the shape.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out NeuronId id)
    {
        id = default;
        if (string.IsNullOrEmpty(text)) { return false; }
        var first = text.IndexOf('/', StringComparison.Ordinal);
        if (first <= 0) { return false; }
        var second = text.IndexOf('/', first + 1);
        if (second < 0) { return false; }
        var brain = text[..first];
        var type = text[(first + 1)..second];
        var key = text[(second + 1)..];
        if (!BrainId.IsValid(brain) || !IsType(type) || key.Length == 0 || key.Any(char.IsControl)) { return false; }
        id = new NeuronId(new BrainId(brain), type, key);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Type is null ? string.Empty : $"{Brain.Value}/{Type}/{Key}";

    private static bool IsType([NotNullWhen(true)] string? type)
        => !string.IsNullOrEmpty(type) && !type.Any(c => char.IsControl(c) || c == '/');

    internal sealed class NeuronIdJsonConverter : JsonConverter<NeuronId>
    {
        public override NeuronId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) { throw new JsonException(Shape); }
            return TryParse(reader.GetString(), out var id) ? id : throw new JsonException(Shape);
        }

        public override void Write(Utf8JsonWriter writer, NeuronId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
