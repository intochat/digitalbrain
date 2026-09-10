using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.UI;

internal static class UIBodies
{
    internal static Signal Signal<T>(string type, T body, JsonTypeInfo<T> json)
        => Abstractions.Signals.Signal.Create(type, JsonSerializer.Serialize(body, json));

    internal static T Read<T>(SignalDelivery delivery, JsonTypeInfo<T> json)
        => JsonSerializer.Deserialize(delivery.Signal.Body, json)
            ?? throw new JsonException($"Signal '{delivery.Signal.Type}' requires a {typeof(T).Name} body.");

    internal static Signal Card(string type, string name, string title)
        => Abstractions.Signals.Signal.Create(type, new JsonObject { ["name"] = name, ["title"] = title }.ToJsonString());
}
