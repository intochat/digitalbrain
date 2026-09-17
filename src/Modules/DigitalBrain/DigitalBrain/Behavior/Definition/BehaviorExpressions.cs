using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core.Behavior;

public static partial class BehaviorExpressions
{
    public static JsonElement Resolve(JsonElement template, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteResolved(writer, template, input, value, outputs, 0);
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    public static JsonElement ResolvePath(string? expression, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        var path = expression?.Trim() ?? string.Empty;
        if (path.StartsWith("{{", StringComparison.Ordinal) && path.EndsWith("}}", StringComparison.Ordinal))
        {
            path = path[2..^2].Trim();
        }

        if (path == "input")
        {
            return Clone(input);
        }

        if (path == "value")
        {
            return Clone(value);
        }

        if (path.StartsWith("input.", StringComparison.Ordinal) || path.StartsWith("input[", StringComparison.Ordinal))
        {
            return ReadPath(input, path[5..]);
        }

        if (path.StartsWith("value.", StringComparison.Ordinal) || path.StartsWith("value[", StringComparison.Ordinal))
        {
            return ReadPath(value, path[5..]);
        }

        if (path.StartsWith("nodes.", StringComparison.Ordinal))
        {
            var reference = path[6..];
            var separator = reference.IndexOfAny(['.', '[']);
            var id = separator < 0 ? reference : reference[..separator];
            return outputs.TryGetValue(id, out var result)
                ? ReadPath(result, separator < 0 ? null : reference[separator..])
                : Null;
        }

        return ReadPath(value, path);
    }

    public static JsonElement ReadPath(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
        {
            return Clone(root);
        }

        var text = path.Trim();
        var offset = text.StartsWith('$') ? 1 : 0;
        var current = root;
        while (offset < text.Length)
        {
            if (text[offset] == '.')
            {
                offset++;
                if (offset == text.Length)
                {
                    throw new ArgumentException($"Path '{path}' ends with an empty field.", nameof(path));
                }
            }

            if (text[offset] == '[')
            {
                var close = text.IndexOf(']', offset + 1);
                if (close < 0)
                {
                    throw new ArgumentException($"Path '{path}' has an unclosed index.", nameof(path));
                }

                var index = text[(offset + 1)..close].Trim();
                if (index.Length >= 2 && (index[0] == '"' && index[^1] == '"' || index[0] == '\'' && index[^1] == '\''))
                {
                    current = Property(current, index[1..^1]);
                }
                else
                {
                    if (!int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
                    {
                        throw new ArgumentException($"Path '{path}' requires a nonnegative array index or quoted field.", nameof(path));
                    }
                    current = current.ValueKind == JsonValueKind.Array && position < current.GetArrayLength()
                        ? current[position]
                        : Null;
                }

                offset = close + 1;
                if (offset < text.Length && text[offset] is not ('.' or '['))
                {
                    throw new ArgumentException($"Path '{path}' is invalid after an index.", nameof(path));
                }
            }
            else
            {
                var start = offset;
                while (offset < text.Length && text[offset] is not ('.' or '['))
                {
                    offset++;
                }

                if (offset == start)
                {
                    throw new ArgumentException($"Path '{path}' contains an empty field.", nameof(path));
                }

                current = Property(current, text[start..offset]);
            }
        }

        return Clone(current);
    }

    internal static JsonElement Null { get; } = CreateNull();

    internal static JsonElement Clone(JsonElement value) => value.ValueKind == JsonValueKind.Undefined ? Null : value.Clone();

    internal static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Undefined or JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };

    private static JsonElement CreateNull()
    {
        using var document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }

    private static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : Null;

    private static void WriteResolved(Utf8JsonWriter writer, JsonElement template, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs, int depth)
    {
        if (depth > 32)
        {
            throw new ArgumentException("Behavior templates may nest at most 32 levels.", nameof(template));
        }

        switch (template.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in template.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteResolved(writer, property.Value, input, value, outputs, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in template.EnumerateArray())
                {
                    WriteResolved(writer, item, input, value, outputs, depth + 1);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = template.GetString() ?? string.Empty;
                var matches = Placeholder().Matches(text);
                if (matches.Count == 1 && matches[0].Index == 0 && matches[0].Length == text.Length)
                {
                    ResolveReference(matches[0].Groups[1].Value, input, value, outputs).WriteTo(writer);
                }
                else
                {
                    var expanded = new StringBuilder();
                    var cursor = 0;
                    foreach (Match match in matches)
                    {
                        expanded.Append(text, cursor, match.Index - cursor);
                        expanded.Append(Text(ResolveReference(match.Groups[1].Value, input, value, outputs)));
                        if (expanded.Length > Signal.MaxBodyBytes)
                        {
                            throw new ArgumentException("A resolved template exceeds the 64 KB signal limit. Return smaller data or an external reference.", nameof(template));
                        }
                        cursor = match.Index + match.Length;
                    }
                    expanded.Append(text, cursor, text.Length - cursor);
                    writer.WriteStringValue(expanded.ToString());
                }
                break;
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                template.WriteTo(writer);
                break;
        }
        RequireOutputRoom(writer);
    }

    internal static void RequireOutputRoom(Utf8JsonWriter writer)
    {
        if (writer.BytesCommitted + writer.BytesPending > Signal.MaxBodyBytes)
        {
            throw new ArgumentException("Behavior output exceeds the 64 KB signal limit. Return smaller data or an external reference.", nameof(writer));
        }
    }

    private static JsonElement ResolveReference(string expression, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs)
    {
        var path = expression.Trim();
        if (path is not ("input" or "value")
            && !path.StartsWith("input.", StringComparison.Ordinal) && !path.StartsWith("input[", StringComparison.Ordinal)
            && !path.StartsWith("value.", StringComparison.Ordinal) && !path.StartsWith("value[", StringComparison.Ordinal)
            && !path.StartsWith("nodes.", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Expression '{{{{{expression}}}}}' must reference input, value, or nodes.<nodeId>.", nameof(expression));
        }

        return ResolvePath(path, input, value, outputs);
    }

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
