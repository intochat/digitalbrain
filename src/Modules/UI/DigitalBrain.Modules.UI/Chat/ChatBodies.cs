using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

internal static class ChatBodies
{
    internal static string? String(JsonNode? body, string name)
        => body is JsonObject json && json[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static IReadOnlyList<ContextRef> Context(JsonNode? body)
        => body is JsonObject json && json["context"] is JsonArray array
            ? [.. array.OfType<JsonObject>()
                .Where(item => !string.IsNullOrWhiteSpace(String(item, "path")) && !string.IsNullOrWhiteSpace(String(item, "schemaHash")))
                .Select(item => new ContextRef(String(item, "path")!, String(item, "schemaHash")!, String(item, "payloadJson"), String(item, "blobRef")))]
            : [];

    internal static string Text(string text) => new JsonObject { ["text"] = text }.ToJsonString();

    internal static string Requested(SendMessage command)
    {
        var body = JsonSerializer.SerializeToNode(command, UIJson.Default.SendMessage)!.AsObject();
        body.Remove("id");
        body.Remove("expectedVersion");
        body["commandId"] = command.Id.ToString();
        return body.ToJsonString();
    }
}
