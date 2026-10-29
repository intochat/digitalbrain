using System.Reflection;
using System.Text.Json;

namespace DigitalBrain.Invocation;

/// <summary>Encodes a contract call's arguments the way every caller does: each non-token argument with its parameter type.</summary>
public static class InvocationEncoder
{
    /// <summary>Encodes <paramref name="args"/> for <paramref name="method"/>, skipping <see cref="CancellationToken"/> parameters.</summary>
    public static JsonElement[] Encode(MethodInfo method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        var parameters = method.GetParameters();
        List<JsonElement> arguments = new(parameters.Length);
        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType == typeof(CancellationToken)) { continue; }
            arguments.Add(JsonSerializer.SerializeToElement(args is not null && index < args.Length ? args[index] : null, parameters[index].ParameterType, RuntimeProtocol.Json));
        }
        return [.. arguments];
    }

    /// <summary>The first <see cref="CancellationToken"/> argument of the call, or none.</summary>
    public static CancellationToken TokenOf(MethodInfo method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        var parameters = method.GetParameters();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType == typeof(CancellationToken) && args is not null && index < args.Length && args[index] is CancellationToken token) { return token; }
        }
        return CancellationToken.None;
    }
}
