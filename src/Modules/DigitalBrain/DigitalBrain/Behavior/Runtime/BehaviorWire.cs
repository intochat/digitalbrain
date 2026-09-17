using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

internal static class BehaviorWire
{
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    internal static JsonElement Null { get; } = JsonSerializer.SerializeToElement<object?>(null);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new ArgumentException("The program signal body is empty.");
}
