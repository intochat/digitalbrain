using System.Reflection;
using System.Text.Json;

namespace DigitalBrain.Client;

// A script's brain.Get<T>(id): every call on the contract becomes one edge invocation.
// DispatchProxy needs a public, non-sealed type with a public constructor.
#pragma warning disable CA1852
public class NeuronProxy : DispatchProxy
#pragma warning restore CA1852
{
    private static readonly MethodInfo InvokeTypedMethod = typeof(NeuronProxy).GetMethod(nameof(InvokeTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private ScriptEdgeClient _edge = null!;

    internal Type Contract { get; private set; } = null!;

    internal string Key { get; private set; } = "";

    internal static T Create<T>(ScriptEdgeClient edge, string key) where T : class
    {
        var proxy = Create<T, NeuronProxy>();
        var neuron = (NeuronProxy)(object)proxy;
        neuron._edge = edge;
        neuron.Contract = typeof(T);
        neuron.Key = key;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var parameters = targetMethod.GetParameters();
        var cancellationToken = CancellationToken.None;
        List<JsonElement> arguments = [];
        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType == typeof(CancellationToken))
            {
                cancellationToken = args?[index] is CancellationToken token ? token : CancellationToken.None;
                continue;
            }
            arguments.Add(JsonSerializer.SerializeToElement(args?[index], parameters[index].ParameterType, ScriptEdgeProtocol.Json));
        }
        var invocation = new ScriptInvocation(Contract.FullName!, Key, targetMethod.Name, [.. arguments]);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task)) { return _edge.InvokeAsync(invocation, cancellationToken); }
        if (returnType == typeof(ValueTask)) { return new ValueTask(_edge.InvokeAsync(invocation, cancellationToken)); }
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return InvokeTypedMethod.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(this, [invocation, cancellationToken]);
        }
        throw new NotSupportedException($"{Contract.Name}.{targetMethod.Name} returns {returnType.Name}; scripts can call methods returning Task or Task<T>.");
    }

    private async Task<TResult> InvokeTypedAsync<TResult>(ScriptInvocation invocation, CancellationToken cancellationToken)
    {
        var value = await _edge.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        return value is { } json ? json.Deserialize<TResult>(ScriptEdgeProtocol.Json)! : default!;
    }
}
