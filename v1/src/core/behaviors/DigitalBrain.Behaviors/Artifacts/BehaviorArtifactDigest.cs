namespace DigitalBrain.Behaviors.Artifacts;

using System.Security.Cryptography;
using System.Text.Json.Serialization;

[GenerateSerializer]
[Alias("db.behavior-artifact-digest")]
public readonly record struct BehaviorArtifactDigest
{
    [JsonConstructor]
    public BehaviorArtifactDigest(string value) => Value = Validate(value);

    [Id(0)]
    public string Value { get; }

    public static BehaviorArtifactDigest Compute(ReadOnlySpan<byte> artifact)
        => new(ToLowerHex(SHA256.HashData(artifact)));

    public override string ToString() => Value;

    private static string Validate(string value)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new FormatException("A behavior artifact digest must be a 64-character lowercase hexadecimal SHA-256 digest.");
        }

        return value;
    }

    private static string ToLowerHex(ReadOnlySpan<byte> bytes)
        => string.Create(bytes.Length * 2, bytes.ToArray(), static (characters, hash) =>
        {
            const string Hex = "0123456789abcdef";

            for (var index = 0; index < hash.Length; index++)
            {
                characters[index * 2] = Hex[hash[index] >> 4];
                characters[(index * 2) + 1] = Hex[hash[index] & 0x0f];
            }
        });
}
