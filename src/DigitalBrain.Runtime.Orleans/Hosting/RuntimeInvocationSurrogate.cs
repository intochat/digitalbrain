using System.Text.Json;
using DigitalBrain.Invocation;
using Orleans;
using Orleans.Serialization.Cloning;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>Orleans wire form of <see cref="RuntimeInvocation"/>: the arguments travel as JSON text. The core stays Orleans-free.</summary>
[GenerateSerializer, Alias("db.runtime-invocation")]
internal struct RuntimeInvocationSurrogate
{
    [Id(0)]
    public string Behavior;

    [Id(1)]
    public string Contract;

    [Id(2)]
    public string Key;

    [Id(3)]
    public string Method;

    [Id(4)]
    public byte[]?[] Arguments;
}

/// <summary>In-silo copies share the invocation: its arguments are encoded once by the caller and never re-encoded for a local call.</summary>
[RegisterCopier]
internal sealed class RuntimeInvocationCopier : IDeepCopier<RuntimeInvocation>
{
    public RuntimeInvocation? DeepCopy(RuntimeInvocation? input, CopyContext context) => input;
}

[RegisterConverter]
internal sealed class RuntimeInvocationSurrogateConverter : IConverter<RuntimeInvocation, RuntimeInvocationSurrogate>
{
    public RuntimeInvocation ConvertFromSurrogate(in RuntimeInvocationSurrogate surrogate)
        => new(surrogate.Behavior ?? string.Empty, surrogate.Contract ?? string.Empty, surrogate.Key ?? string.Empty, surrogate.Method ?? string.Empty,
            surrogate.Arguments is null ? [] : [.. surrogate.Arguments.Select(JsonElementSurrogateConverter.Parse)]);

    public RuntimeInvocationSurrogate ConvertToSurrogate(in RuntimeInvocation value) => new()
    {
        Behavior = value.Behavior,
        Contract = value.Contract,
        Key = value.Key,
        Method = value.Method,
        Arguments = value.Arguments is null ? [] : [.. value.Arguments.Select(JsonElementSurrogateConverter.Utf8)],
    };
}
