using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain.Invocation;

/// <summary>
/// The typed <c>Get&lt;T&gt;(NeuronId)</c> proxy: every call on the contract becomes one <see cref="RuntimeInvocation"/> handed to an
/// <see cref="IInvocationSink"/>. One-way calls post, streams stream, everything else invokes; <see cref="CallTimeoutAttribute"/> bounds the wait.
/// </summary>
/// <remarks>DispatchProxy needs a public, non-sealed type with a public constructor; create instances with <see cref="Create{T}"/>.</remarks>
#pragma warning disable CA1852
public class NeuronProxy : DispatchProxy
#pragma warning restore CA1852
{
    private static readonly ConcurrentDictionary<Type, string> ContractNames = new();
    private static readonly ConcurrentDictionary<MethodInfo, ProxyMethod> Methods = new();
    private static readonly MethodInfo InvokeValueMethod = typeof(NeuronProxy).GetMethod(nameof(InvokeValue), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo InvokeValueTaskMethod = typeof(NeuronProxy).GetMethod(nameof(InvokeValueTask), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo StreamMethod = typeof(NeuronProxy).GetMethod(nameof(StreamItems), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private IInvocationSink _sink = null!;
    private string _contract = "";
    private string _key = "";

    /// <summary>The neuron this proxy calls.</summary>
    public NeuronId Target { get; private set; }

    /// <summary>A proxy for contract <typeparamref name="T"/> on neuron <paramref name="id"/> that hands calls to <paramref name="sink"/>.</summary>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is not an interface.</exception>
    public static T Create<T>(NeuronId id, IInvocationSink sink) where T : class
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (!typeof(T).IsInterface) { throw new ArgumentException($"{typeof(T).Name} is not an interface; neuron contracts are interfaces.", nameof(T)); }
        var proxy = Create<T, NeuronProxy>();
        var neuron = (NeuronProxy)(object)proxy;
        neuron._sink = sink;
        neuron._contract = ContractName(typeof(T));
        neuron.Target = id;
        neuron._key = id.ToString();
        return proxy;
    }

    /// <summary>The wire name of a contract: its <see cref="NeuronAliasAttribute"/> alias, or its full name.</summary>
    public static string ContractName(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return ContractNames.GetOrAdd(contract, static type => type.GetCustomAttribute<NeuronAliasAttribute>(inherit: false)?.Alias ?? type.FullName ?? type.Name);
    }

    /// <summary>Two proxies for one neuron are equal.</summary>
    public override bool Equals(object? obj) => obj is NeuronProxy other && Target == other.Target && _contract == other._contract;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Target, _contract);

    /// <inheritdoc />
    public override string ToString() => $"{_contract}@{Target}";

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.DeclaringType == typeof(object))
        {
            return targetMethod.Name switch
            {
                nameof(Equals) => Equals(args?[0]),
                nameof(GetHashCode) => GetHashCode(),
                nameof(ToString) => ToString(),
                _ => throw new NotSupportedException(targetMethod.Name),
            };
        }
        // Bound once per contract method: parameter types, token position, timeout and the dispatch delegate; no per-call reflection.
        var bound = Methods.GetOrAdd(targetMethod, static method => new ProxyMethod(method, Dispatch(method)));
        var call = new RuntimeInvocation(string.Empty, _contract, _key, targetMethod.Name, bound.Encode(args));
        return bound.Dispatch(this, call, bound.Shape, bound.TokenOf(args));
    }

    private sealed class ProxyMethod
    {
        private readonly Type[] _parameters;
        private readonly int _token = -1;

        public ProxyMethod(MethodInfo method, Func<NeuronProxy, RuntimeInvocation, CallShape, CancellationToken, object?> dispatch)
        {
            _parameters = [.. method.GetParameters().Select(parameter => parameter.ParameterType)];
            _token = Array.IndexOf(_parameters, typeof(CancellationToken));
            ArgumentCount = _parameters.Count(type => type != typeof(CancellationToken));
            Shape = new CallShape(method.GetCustomAttribute<CallTimeoutAttribute>()?.Timeout, method.Name);
            Dispatch = dispatch;
        }

        public int ArgumentCount { get; }

        public CallShape Shape { get; }

        public Func<NeuronProxy, RuntimeInvocation, CallShape, CancellationToken, object?> Dispatch { get; }

        public JsonElement[] Encode(object?[]? args)
        {
            if (ArgumentCount == 0) { return []; }
            var arguments = new JsonElement[ArgumentCount];
            for (int index = 0, argument = 0; index < _parameters.Length; index++)
            {
                if (_parameters[index] == typeof(CancellationToken)) { continue; }
                arguments[argument++] = PooledJson.Encode(args is not null && index < args.Length ? args[index] : null, _parameters[index]);
            }
            return arguments;
        }

        public CancellationToken TokenOf(object?[]? args)
            => _token >= 0 && args is not null && _token < args.Length && args[_token] is CancellationToken token ? token : CancellationToken.None;
    }

    private static Func<NeuronProxy, RuntimeInvocation, CallShape, CancellationToken, object?> Dispatch(MethodInfo method)
    {
        var returnType = method.ReturnType;
        var definition = returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : null;
        if (method.GetCustomAttribute<OneWayCallAttribute>() is not null)
        {
            if (returnType == typeof(Task)) { return static (proxy, call, _, _) => proxy._sink.Post(proxy.Target, call); }
            if (returnType == typeof(ValueTask)) { return static (proxy, call, _, _) => new ValueTask(proxy._sink.Post(proxy.Target, call)); }
            if (returnType == typeof(void)) { return static (proxy, call, _, _) => { _ = proxy._sink.Post(proxy.Target, call); return null; }; }
            throw new NotSupportedException($"{method.DeclaringType?.Name}.{method.Name} is one-way and so returns void, Task or ValueTask.");
        }
        if (returnType == typeof(Task)) { return static (proxy, call, shape, token) => proxy.InvokeNone(call, shape, token); }
        if (returnType == typeof(ValueTask)) { return static (proxy, call, shape, token) => new ValueTask(proxy.InvokeNone(call, shape, token)); }
        if (definition == typeof(Task<>)) { return Generic(InvokeValueMethod, returnType); }
        if (definition == typeof(ValueTask<>)) { return Generic(InvokeValueTaskMethod, returnType); }
        if (definition == typeof(IAsyncEnumerable<>)) { return Generic(StreamMethod, returnType); }
        throw new NotSupportedException($"{method.DeclaringType?.Name}.{method.Name} returns {returnType.Name}; neuron calls return Task, Task<T>, ValueTask, ValueTask<T> or IAsyncEnumerable<T>.");
    }

    private static Func<NeuronProxy, RuntimeInvocation, CallShape, CancellationToken, object?> Generic(MethodInfo open, Type returnType)
    {
        var closed = open.MakeGenericMethod(returnType.GetGenericArguments()[0])
            .CreateDelegate<Func<NeuronProxy, RuntimeInvocation, CallShape, CancellationToken, object?>>();
        return closed;
    }

    private async Task<JsonElement?> Send(RuntimeInvocation call, CallShape shape, CancellationToken cancellationToken)
    {
        if (shape.Timeout is not { } timeout) { return await _sink.Invoke(Target, call, cancellationToken).ConfigureAwait(false); }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try { return await _sink.Invoke(Target, call, limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{_contract}.{shape.Method} on {Target} did not answer within {timeout}.", error);
        }
    }

    private async Task InvokeNone(RuntimeInvocation call, CallShape shape, CancellationToken cancellationToken)
        => await Send(call, shape, cancellationToken).ConfigureAwait(false);

    // CA1859: these bind to one delegate shape, so they return object.
#pragma warning disable CA1859
    private object? InvokeValue<TResult>(RuntimeInvocation call, CallShape shape, CancellationToken cancellationToken)
        => Typed<TResult>(call, shape, cancellationToken);

    private object? InvokeValueTask<TResult>(RuntimeInvocation call, CallShape shape, CancellationToken cancellationToken)
        => new ValueTask<TResult>(Typed<TResult>(call, shape, cancellationToken));

#pragma warning restore CA1859

    private async Task<TResult> Typed<TResult>(RuntimeInvocation call, CallShape shape, CancellationToken cancellationToken)
    {
        var value = await Send(call, shape, cancellationToken).ConfigureAwait(false);
        return value is { } json ? json.Deserialize<TResult>(RuntimeProtocol.Json)! : default!;
    }

    private object? StreamItems<TItem>(RuntimeInvocation call, CallShape shape, CancellationToken cancellationToken)
        => Items<TItem>(call, cancellationToken);

    private async IAsyncEnumerable<TItem> Items<TItem>(RuntimeInvocation call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _sink.Stream(Target, call, cancellationToken).ConfigureAwait(false))
        {
            yield return item.Deserialize<TItem>(RuntimeProtocol.Json)!;
        }
    }

    private readonly record struct CallShape(TimeSpan? Timeout, string Method);
}
