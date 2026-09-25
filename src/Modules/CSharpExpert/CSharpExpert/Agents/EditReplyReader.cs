using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

public static class EditReplyReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<EditRequest> Read(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var start = reply.IndexOf('[', StringComparison.Ordinal);
        var end = reply.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            throw new FormatException("The implementer reply has no JSON array of edits.");
        }

        try
        {
            return JsonSerializer.Deserialize<EditRequest[]>(reply[start..(end + 1)], Json) ?? [];
        }
        catch (JsonException error)
        {
            throw new FormatException($"The implementer reply is not a valid edit list: {error.Message}", error);
        }
    }
}
