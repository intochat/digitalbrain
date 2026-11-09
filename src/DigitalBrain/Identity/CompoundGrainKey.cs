using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Identity;

/// <summary>
/// Strongly typed, structured Orleans compound grain identity: {Brain}/{Type}/{Key}.
/// Eliminates ad-hoc slash-splitting and ensures allocation-free parsing and identity validation.
/// </summary>
public readonly record struct CompoundGrainKey(BrainId Brain, string Type, string Key) : IEquatable<CompoundGrainKey>
{
    public BrainId Brain { get; } = Brain.Value is not null ? Brain : throw new ArgumentException("A compound grain key requires a valid brain id.", nameof(Brain));
    public string Type { get; } = !string.IsNullOrWhiteSpace(Type) && !Type.Contains('/') ? Type : throw new ArgumentException("Neuron type name cannot be empty or contain slashes.", nameof(Type));
    public string Key { get; } = !string.IsNullOrWhiteSpace(Key) ? Key : throw new ArgumentException("Neuron key cannot be empty.", nameof(Key));

    public static CompoundGrainKey Create(string brainId, string type, string key) =>
        new(new BrainId(brainId), type, key);

    public static CompoundGrainKey Parse(string text) =>
        TryParse(text, out var key) ? key : throw new FormatException($"Invalid compound grain key format: '{text}'. Expected 'brain/type/key'.");

    public static bool TryParse([NotNullWhen(true)] string? text, out CompoundGrainKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text)) { return false; }

        var first = text.IndexOf('/', StringComparison.Ordinal);
        if (first <= 0) { return false; }

        var second = text.IndexOf('/', first + 1, StringComparison.Ordinal);
        if (second <= first + 1 || second >= text.Length - 1) { return false; }

        var brain = text[..first];
        var type = text[(first + 1)..second];
        var keyPart = text[(second + 1)..];

        if (!IsValidBrainIdentifier(brain))
        {
            return false;
        }

        BrainId brainId = brain.StartsWith('@') ? BrainId.System(brain[1..]) : new BrainId(brain);

        if (string.IsNullOrWhiteSpace(type) || type.Contains('/') || type.Any(char.IsControl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(keyPart) || keyPart.StartsWith('/') || keyPart.EndsWith('/') || keyPart.Contains("//", StringComparison.Ordinal) || keyPart.Any(char.IsControl))
        {
            return false;
        }

        key = new CompoundGrainKey(brainId, type, keyPart);
        return true;
    }

    private static bool IsValidBrainIdentifier(string brain)
    {
        if (string.IsNullOrWhiteSpace(brain) || brain.Length > 200) return false;
        var span = brain.StartsWith('@') ? brain.AsSpan(1) : brain.AsSpan();
        if (span.IsEmpty || span.IsWhiteSpace()) return false;
        foreach (var c in span)
        {
            if (char.IsControl(c) || c is '/' or '\\' or ':' or '!' or '?' or '*' or '<' or '>' or '|' or '"' or '@')
            {
                return false;
            }
        }
        return true;
    }

    public NeuronId ToNeuronId() => new(Brain, Type, Key);

    public static CompoundGrainKey FromNeuronId(NeuronId neuronId) =>
        new(neuronId.Brain, neuronId.Type, neuronId.Key);

    public override string ToString() => $"{Brain.Value}/{Type}/{Key}";
}
