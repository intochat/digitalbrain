using System.Collections.Concurrent;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.Invocation;

/// <summary>The wire options every invocation is encoded and decoded with.</summary>
public static class RuntimeProtocol
{
    private static readonly RuntimeTypeInfoResolver Resolver = new();

    /// <summary>
    /// Web defaults with a depth of 128: a bounded UI tree of 32 nodes, each with a children array, fits inside an invocation.
    /// Types resolve through registered contexts, then the runtime's source-generated context, then reflection.
    /// </summary>
    /// <remarks>Never serialize types from collectible load contexts (generated synapse sources) through these options: the System.Text.Json cache would pin the context and block unloading. Use <see cref="OptionsFor(Type)"/> and <see cref="Forget"/> instead.</remarks>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 128, TypeInfoResolver = Resolver };

    /// <summary>
    /// Registers a contract's source-generated <c>JsonSerializerContext</c> (or any resolver) ahead of reflection. Register before the
    /// first call that uses its types: metadata already resolved for a type is cached by <see cref="Json"/>.
    /// </summary>
    public static void Register(IJsonTypeInfoResolver resolver) => Resolver.Register(resolver);

    private static readonly ConcurrentDictionary<AssemblyLoadContext, JsonSerializerOptions> PerContext = new();

    /// <summary>
    /// The options to (de)serialize <paramref name="type"/> with: <see cref="Json"/> for a type of non-collectible contexts; for a type
    /// that involves a collectible <see cref="AssemblyLoadContext"/> (a generated synapse source), options of that context cloned from
    /// <see cref="Json"/> whose metadata for the context's types uses reflection accessors only, so nothing they cache outlives
    /// <see cref="Forget"/> and keeps the context from unloading.
    /// </summary>
    /// <param name="type">The type being serialized; pass the runtime type when the declared one is not collectible.</param>
    /// <returns>The options.</returns>
    public static JsonSerializerOptions OptionsFor(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsCollectible || CollectibleContextOf(type) is not { } context) { return Json; }
        return PerContext.GetOrAdd(context, static _ => new JsonSerializerOptions(Json) { TypeInfoResolver = new CollectibleTypeInfoResolver(Resolver) });
    }

    /// <summary>Drops the options of <paramref name="context"/>; call before unloading it.</summary>
    /// <param name="context">The collectible context.</param>
    public static void Forget(AssemblyLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = PerContext.TryRemove(context, out _);
    }

    private static AssemblyLoadContext? CollectibleContextOf(Type type)
    {
        if (type.Assembly.IsCollectible) { return AssemblyLoadContext.GetLoadContext(type.Assembly); }
        if (type.HasElementType && CollectibleContextOf(type.GetElementType()!) is { } element) { return element; }
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (CollectibleContextOf(argument) is { } found) { return found; }
            }
        }
        return null;
    }
}
