using DigitalBrain.Qdrant.Query;

namespace DigitalBrain.Qdrant;

internal static class QdrantPointRules
{
    public const int MaxPayloadFields = 32;
    public const int MaxFieldNameLength = 128;
    public const int MaxFieldValueLength = 4096;
    public const int MaxVectorSize = 4096;

    public static Guid RequirePointId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParseExact(id.Trim(), "D", out var parsed) || parsed == Guid.Empty)
        {
            throw new QdrantQueryException("id must be a UUID.");
        }

        return parsed;
    }

    public static void RequireVector(float[]? vector, int expectedSize)
    {
        if (vector is null || vector.Length != expectedSize)
        {
            throw new QdrantQueryException($"vector must contain {expectedSize} dimensions.");
        }
    }

    public static void RequireFields(IReadOnlyList<QdrantField>? fields)
    {
        if (fields is null)
        {
            throw new QdrantQueryException("payload is required.");
        }

        if (fields.Count > MaxPayloadFields)
        {
            throw new QdrantQueryException($"payload must contain at most {MaxPayloadFields} fields.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field is null || !IsFieldName(field.Name))
            {
                throw new QdrantQueryException("payload field names must be letters, digits, underscores or hyphens.");
            }

            if (field.Value is null || field.Value.Length > MaxFieldValueLength)
            {
                throw new QdrantQueryException($"payload field '{field.Name}' is too long.");
            }

            if (!seen.Add(field.Name))
            {
                throw new QdrantQueryException($"payload field '{field.Name}' is duplicated.");
            }
        }
    }

    public static bool IsCollectionName(string? name)
        => !string.IsNullOrWhiteSpace(name)
            && name.Length <= 255
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool IsFieldName(string? name)
        => !string.IsNullOrWhiteSpace(name)
            && name.Length <= MaxFieldNameLength
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}