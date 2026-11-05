using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;

namespace DigitalBrain.Client;

#pragma warning disable CA1852
public class NeuronProxy : DispatchProxy
#pragma warning restore CA1852
{
    private static readonly MethodInfo InvokeTypedMethod = typeof(NeuronProxy).GetMethod(nameof(InvokeTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private SessionConnection _session = null!;
    private string _behavior = "";
    private string? _traceParent;

    public Type Contract { get; private set; } = null!;

    public string Key { get; private set; } = "";

    public static T Create<T>(SessionConnection session, string behavior, string key, string? traceParent = null) where T : class
        => (T)Create(typeof(T), session, behavior, key, traceParent);

    public static object Create(Type contract, SessionConnection session, string behavior, string key, string? traceParent = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(key);

        var proxy = Create(contract, typeof(NeuronProxy));
        var neuron = (NeuronProxy)proxy;
        neuron._session = session;
        neuron._behavior = behavior;
        neuron._traceParent = traceParent;
        neuron.Contract = contract;
        neuron.Key = key;
        return proxy;
    }

    public override bool Equals(object? obj) => obj is NeuronProxy other && string.Equals(Key, other.Key, StringComparison.Ordinal) && Contract == other.Contract;

    public override int GetHashCode() => HashCode.Combine(Key, Contract);

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
            arguments.Add(JsonSerializer.SerializeToElement(args?[index], parameters[index].ParameterType, RuntimeProtocol.Json));
        }
        var contractName = Contract.GetCustomAttribute<NeuronAliasAttribute>(inherit: false)?.Alias ?? Contract.FullName ?? Contract.Name;
        var invocation = new RuntimeInvocation(_behavior, contractName, Key, targetMethod.Name, [.. arguments]);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task)) { return _session.InvokeAsync(invocation, _traceParent, cancellationToken); }
        if (returnType == typeof(ValueTask)) { return new ValueTask(_session.InvokeAsync(invocation, _traceParent, cancellationToken)); }
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return InvokeTypedMethod.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(this, [invocation, cancellationToken]);
        }
        throw new NotSupportedException($"{Contract.Name}.{targetMethod.Name} returns {returnType.Name}; client calls support methods returning Task or Task<T>.");
    }

    private async Task<TResult> InvokeTypedAsync<TResult>(RuntimeInvocation invocation, CancellationToken cancellationToken)
    {
        var value = await _session.InvokeAsync(invocation, _traceParent, cancellationToken).ConfigureAwait(false);
        return value is { } json ? json.Deserialize<TResult>(RuntimeProtocol.Json)! : default!;
    }
}
