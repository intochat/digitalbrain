using System.Text;
using System.Text.Json;

namespace Ino.NeuronTesting;

public sealed class RfwSnapshot
{
    RfwSnapshot(string description, JsonElement data)
    {
        Description = description;
        Data = data;
    }

    public string Description { get; }
    public JsonElement Data { get; }

    public static RfwSnapshot FromBytes(ReadOnlySpan<byte> descriptionBytes, ReadOnlySpan<byte> dataBytes)
    {
        var description = Encoding.UTF8.GetString(descriptionBytes);
        var reader = new Utf8JsonReader(dataBytes);
        using var doc = JsonDocument.ParseValue(ref reader);
        return new RfwSnapshot(description, doc.RootElement.Clone());
    }

    public bool ContainsWidgets(params string[] widgetNames)
    {
        if (widgetNames.Length == 0)
            throw new ArgumentException("At least one widget name is required", nameof(widgetNames));
        foreach (var name in widgetNames)
            if (!Description.Contains(name, StringComparison.Ordinal)) return false;
        return true;
    }

    public T? DataAt<T>(string dottedPath)
    {
        if (string.IsNullOrEmpty(dottedPath))
            throw new ArgumentException("Path must be non-empty", nameof(dottedPath));
        var current = Data;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(segment, out var next)) return default;
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength()) return default;
                current = current[index];
            }
            else return default;
        }
        return current.ValueKind == JsonValueKind.Null ? default : current.Deserialize<T>();
    }
}
