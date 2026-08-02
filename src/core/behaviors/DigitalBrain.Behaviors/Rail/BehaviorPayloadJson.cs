using System.Text.Json;

namespace DigitalBrain.Behaviors;

internal static class BehaviorPayloadJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static byte[] Serialize(object value, Type runtimeType)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(runtimeType);
        return JsonSerializer.SerializeToUtf8Bytes(value, runtimeType, Options);
    }

    public static object? Deserialize(ReadOnlySpan<byte> utf8Json, Type returnType)
        => JsonSerializer.Deserialize(utf8Json, returnType, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize<T>(utf8Json, Options);

    private static JsonSerializerOptions CreateOptions()
        => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
}
