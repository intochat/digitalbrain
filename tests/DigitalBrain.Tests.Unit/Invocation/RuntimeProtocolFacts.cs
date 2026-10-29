using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Invocation;

namespace DigitalBrain.Tests.Unit.Invocation;

public sealed record RegisteredShape(int Value);

[JsonSerializable(typeof(RegisteredShape))]
internal sealed partial class RegisteredShapeContext : JsonSerializerContext;

// 04-14b: the runtime's envelope types resolve from source-generated metadata, and a contract can register its own context.
public sealed class RuntimeProtocolFacts
{
    [Fact]
    public void A_registered_context_resolves_its_types_and_the_runtime_envelope_is_source_generated()
    {
        var counting = new Counting(RegisteredShapeContext.Default);
        RuntimeProtocol.Register(counting);
        RuntimeProtocol.Register(counting);
        Assert.Equal("{\"value\":3}", JsonSerializer.Serialize(new RegisteredShape(3), RuntimeProtocol.Json));
        Assert.True(counting.Hits > 0);

        var call = new RuntimeInvocation("", "c", "b/t/k", "M", [JsonSerializer.SerializeToElement(1, RuntimeProtocol.Json)]);
        var back = JsonSerializer.Deserialize<RuntimeInvocation>(JsonSerializer.Serialize(call, RuntimeProtocol.Json), RuntimeProtocol.Json)!;
        Assert.Equal(1, back.Arguments[0].GetInt32());
    }

    private sealed class Counting(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
    {
        public int Hits;

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var info = inner.GetTypeInfo(type, options);
            if (info is not null) { Hits++; }
            return info;
        }
    }
}
