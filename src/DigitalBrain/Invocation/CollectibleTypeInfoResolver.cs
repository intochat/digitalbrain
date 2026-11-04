using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.Invocation;

/// <summary>
/// The resolver of a collectible context's options (<see cref="RuntimeProtocol.OptionsFor"/>): a type from a collectible assembly gets
/// metadata whose constructor and property accessors are plain reflection. The default resolver emits them through System.Text.Json's
/// process-wide accessor cache, which would keep the context alive after it unloads. Every other type goes to <paramref name="inner"/>.
/// </summary>
/// <param name="inner">The runtime resolver chain.</param>
internal sealed class CollectibleTypeInfoResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        if (!type.IsCollectible) { return inner.GetTypeInfo(type, options); }
        var info = JsonTypeInfo.CreateJsonTypeInfo(type, options);
        if (!type.IsAbstract && !type.IsInterface && !type.IsValueType && type.GetConstructor(Type.EmptyTypes) is { } constructor)
        {
            info.CreateObject = () => constructor.Invoke(null);
        }
        if (info.Kind != JsonTypeInfoKind.Object) { return info; }
        if (info.CreateObject is null && !type.IsAbstract && !type.IsInterface)
        {
            // A positional record: created blank, then every property is set through its init accessor.
            info.CreateObject = () => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true } || property.IsDefined(typeof(JsonIgnoreAttribute), true))
            {
                continue;
            }
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? options.PropertyNamingPolicy?.ConvertName(property.Name)
                ?? property.Name;
            var json = info.CreateJsonPropertyInfo(property.PropertyType, name);
            var reflected = property;
            json.Get = target => reflected.GetValue(target);
            if (property.SetMethod is not null)
            {
                json.Set = (target, value) => reflected.SetValue(target, value);
            }
            info.Properties.Add(json);
        }
        return info;
    }
}
