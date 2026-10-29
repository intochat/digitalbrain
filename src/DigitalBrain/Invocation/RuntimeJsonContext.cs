using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.Invocation;

/// <summary>Source-generated metadata for the runtime's own envelope types and the common primitive arguments (06 D-12 stays JSON).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RuntimeInvocation))]
[JsonSerializable(typeof(Gap))]
[JsonSerializable(typeof(NeuronId))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class RuntimeJsonContext : JsonSerializerContext;

/// <summary>
/// The resolver behind <see cref="RuntimeProtocol.Json"/>: registered contexts first, then the runtime's own context, then reflection.
/// </summary>
internal sealed class RuntimeTypeInfoResolver : IJsonTypeInfoResolver
{
    private static readonly DefaultJsonTypeInfoResolver Reflection = new();
    private IJsonTypeInfoResolver[] _registered = [];

    public void Register(IJsonTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        IJsonTypeInfoResolver[] current, next;
        do
        {
            current = Volatile.Read(ref _registered);
            if (Array.IndexOf(current, resolver) >= 0) { return; }
            next = [.. current, resolver];
        }
        while (Interlocked.CompareExchange(ref _registered, next, current) != current);
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        foreach (var resolver in Volatile.Read(ref _registered))
        {
            if (resolver.GetTypeInfo(type, options) is { } info) { return info; }
        }
        return ((IJsonTypeInfoResolver)RuntimeJsonContext.Default).GetTypeInfo(type, options) ?? Reflection.GetTypeInfo(type, options);
    }
}
