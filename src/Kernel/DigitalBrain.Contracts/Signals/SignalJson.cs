using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.Abstractions.Signals;

public sealed partial record Signal
{
    public static Signal FromJson<T>(string type, T body, JsonTypeInfo<T> json)
        => Create(type, JsonSerializer.Serialize(body, json));
}

public static class SignalJson
{
    public static T? Body<T>(this SignalDelivery delivery, JsonTypeInfo<T> json) where T : class
    {
        ArgumentNullException.ThrowIfNull(delivery);
        try
        {
            return JsonSerializer.Deserialize(delivery.Signal.Body, json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
