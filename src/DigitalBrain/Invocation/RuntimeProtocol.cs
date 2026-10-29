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
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 128, TypeInfoResolver = Resolver };

    /// <summary>
    /// Registers a contract's source-generated <c>JsonSerializerContext</c> (or any resolver) ahead of reflection. Register before the
    /// first call that uses its types: metadata already resolved for a type is cached by <see cref="Json"/>.
    /// </summary>
    public static void Register(IJsonTypeInfoResolver resolver) => Resolver.Register(resolver);
}
