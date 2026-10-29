using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DigitalBrain.Invocation;

/// <summary>
/// The one binder from a <see cref="RuntimeInvocation"/> to a contract method: by name, argument count and parameter types, with
/// cached compiled invokers. TestOS, the host runtime and the edge all dispatch through it.
/// </summary>
/// <remarks>Only public instance methods of the contract bind; members of object, IDisposable and IAsyncDisposable never do.</remarks>
public sealed class NeuronBinder
{
    /// <summary>The default bound on the encoded size of one invocation's arguments: 1 MiB.</summary>
    public const int DefaultMaximumArgumentBytes = 1024 * 1024;

    private static readonly MethodInfo ItemsMethod = typeof(NeuronBinder).GetMethod(nameof(Items), BindingFlags.NonPublic | BindingFlags.Static)!;
    private readonly ConcurrentDictionary<(Type Contract, string Method, int Arity), BoundCall[]> _calls = new();
    private long _misses;

    /// <summary>Creates a binder that refuses arguments larger than <paramref name="maximumArgumentBytes"/> in total.</summary>
    public NeuronBinder(int maximumArgumentBytes = DefaultMaximumArgumentBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumArgumentBytes);
        MaximumArgumentBytes = maximumArgumentBytes;
    }

    /// <summary>The bound on the encoded size of one invocation's arguments.</summary>
    public int MaximumArgumentBytes { get; }

    /// <summary>How many binds had to reflect over the contract (cache misses).</summary>
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>Resolves the method <paramref name="call"/> names on <paramref name="contract"/>.</summary>
    /// <exception cref="BindingException">No method, more than one, wrong arguments or arguments over the bound.</exception>
    public BoundCall Bind(Type contract, RuntimeInvocation call)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (call is null || call.Method is null || call.Arguments is null) { throw new BindingException($"{contract.Name}: an invocation needs a method and arguments."); }
        long bytes = 0;
        foreach (var argument in call.Arguments)
        {
            if (argument.ValueKind == JsonValueKind.Undefined) { throw new BindingException($"{contract.Name}.{call.Method}: an argument is missing."); }
            bytes += JsonMarshal.GetRawUtf8Value(argument).Length;
        }
        if (bytes > MaximumArgumentBytes) { throw new BindingException($"{contract.Name}.{call.Method}: arguments of {bytes} bytes exceed the bound of {MaximumArgumentBytes}."); }

        var key = (contract, call.Method, call.Arguments.Length);
        if (!_calls.TryGetValue(key, out var candidates))
        {
            Interlocked.Increment(ref _misses);
            candidates = _calls.GetOrAdd(key, Candidates(contract, call.Method, call.Arguments.Length));
        }
        if (candidates.Length == 0) { throw new BindingException($"{contract.Name} has no method {call.Method} taking {call.Arguments.Length} arguments."); }
        if (candidates.Length == 1) { return candidates[0]; }

        var matching = candidates.Where(candidate => Accepts(candidate, call.Arguments)).Take(2).ToArray();
        return matching.Length switch
        {
            1 => matching[0],
            0 => throw new BindingException($"{contract.Name}.{call.Method}: the arguments match no overload."),
            _ => throw new BindingException($"{contract.Name}.{call.Method}: the arguments match more than one overload."),
        };
    }

    /// <summary>Calls <paramref name="call"/> on <paramref name="target"/> and returns its JSON result, or null for no result.</summary>
    /// <exception cref="BindingException">The target or arguments do not fit the call, or the call returns a stream.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Dispatch is per binder: a runtime holds one binder and its cache.")]
    public async Task<JsonElement?> InvokeAsync(object target, BoundCall call, JsonElement[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Kind == InvocationResultKind.Stream) { throw new BindingException($"{call.Contract.Name}.{call.Method.Name} returns a stream; stream it."); }
        var returned = Call(target, call, arguments, cancellationToken);
        var result = call.IsTask ? await TaskBoxing.ResultAsync((Task)returned!, call.TaskResultType).ConfigureAwait(true) : returned;
        return call.ResultType is null ? null : PooledJson.Encode(result, call.ResultType);
    }

    /// <summary>Calls a streaming <paramref name="call"/> and yields its items as JSON until the stream ends or the caller cancels.</summary>
    /// <exception cref="BindingException">The target or arguments do not fit the call, or the call does not return a stream.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Dispatch is per binder: a runtime holds one binder and its cache.")]
    public IAsyncEnumerable<JsonElement> StreamAsync(object target, BoundCall call, JsonElement[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Kind != InvocationResultKind.Stream) { throw new BindingException($"{call.Contract.Name}.{call.Method.Name} does not return a stream."); }
        var returned = Call(target, call, arguments, cancellationToken)
            ?? throw new InvalidOperationException($"{call.Contract.Name}.{call.Method.Name} returned no stream.");
        return Encode(call, call.Streamer!(returned, cancellationToken), cancellationToken);
    }

    private static async IAsyncEnumerable<JsonElement> Encode(BoundCall call, IAsyncEnumerable<object?> items, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in items.ConfigureAwait(true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return JsonSerializer.SerializeToElement(item, call.ResultType!, RuntimeProtocol.Json);
        }
    }

    private static object? Call(object target, BoundCall call, JsonElement[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!call.Contract.IsInstanceOfType(target)) { throw new BindingException($"The target is not a {call.Contract.Name}."); }
        if (arguments is null || arguments.Length != call.ArgumentCount)
        {
            throw new BindingException($"{call.Contract.Name}.{call.Method.Name} takes {call.ArgumentCount} arguments.");
        }
        var values = new object?[call.Parameters.Length];
        for (int index = 0, argument = 0; index < values.Length; index++)
        {
            var type = call.Parameters[index];
            if (type == typeof(CancellationToken)) { values[index] = cancellationToken; continue; }
            try { values[index] = arguments[argument++].Deserialize(type, RuntimeProtocol.Json); }
            catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new BindingException($"{call.Contract.Name}.{call.Method.Name}: argument {argument - 1} is not a {type.Name}.", error);
            }
            if (values[index] is null && type.IsValueType && Nullable.GetUnderlyingType(type) is null)
            {
                throw new BindingException($"{call.Contract.Name}.{call.Method.Name}: argument {argument - 1} cannot be null.");
            }
        }
        try { return call.Invoker(target, values); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static bool Accepts(BoundCall candidate, JsonElement[] arguments)
    {
        var argument = 0;
        foreach (var type in candidate.Parameters)
        {
            if (type == typeof(CancellationToken)) { continue; }
            try { _ = arguments[argument++].Deserialize(type, RuntimeProtocol.Json); }
            catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException) { return false; }
        }
        return true;
    }

    private static BoundCall[] Candidates(Type contract, string method, int arity)
        => Methods(contract)
            .Where(candidate => candidate.Name == method && candidate.GetParameters().Count(parameter => parameter.ParameterType != typeof(CancellationToken)) == arity)
            .Select(candidate => Create(contract, candidate))
            .ToArray();

    private static IEnumerable<MethodInfo> Methods(Type contract)
    {
        IEnumerable<MethodInfo> methods = contract.IsInterface
            ? contract.GetMethods().Concat(contract.GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
            : contract.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        HashSet<MethodInfo> excluded = contract.IsInterface ? [] : contract.GetInterfaces()
            .Where(inherited => inherited == typeof(IDisposable) || inherited == typeof(IAsyncDisposable))
            .SelectMany(inherited => contract.GetInterfaceMap(inherited).TargetMethods)
            .ToHashSet();
        return methods.Where(method => !method.IsSpecialName && !method.IsGenericMethodDefinition && !method.IsStatic
            && method.DeclaringType != typeof(object) && method.DeclaringType != typeof(IDisposable) && method.DeclaringType != typeof(IAsyncDisposable)
            && !excluded.Contains(method)
            && method.GetParameters().All(parameter => !parameter.ParameterType.IsByRef));
    }

    private static BoundCall Create(Type contract, MethodInfo method)
    {
        var returnType = method.ReturnType;
        var definition = returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : null;
        Type? resultType = null, taskResultType = null;
        var isTask = false;
        var kind = InvocationResultKind.None;
        Func<object, CancellationToken, IAsyncEnumerable<object?>>? streamer = null;
        if (returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask))
        {
            isTask = returnType != typeof(void);
        }
        else if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
        {
            isTask = true;
            kind = InvocationResultKind.Value;
            resultType = taskResultType = returnType.GetGenericArguments()[0];
        }
        else if (definition == typeof(IAsyncEnumerable<>))
        {
            kind = InvocationResultKind.Stream;
            resultType = returnType.GetGenericArguments()[0];
            streamer = ItemsMethod.MakeGenericMethod(resultType).CreateDelegate<Func<object, CancellationToken, IAsyncEnumerable<object?>>>();
        }
        else
        {
            kind = InvocationResultKind.Value;
            resultType = returnType;
        }
        return new BoundCall(contract, method, kind, resultType, taskResultType, isTask, Invoker(method), streamer);
    }

    private static bool IsValueTask(Type type)
        => type == typeof(ValueTask) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    // (target, values) => (object?)((Declaring)target).Method((P0)values[0], ...), with ValueTask shapes turned into tasks.
    private static Func<object, object?[], object?> Invoker(MethodInfo method)
    {
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            var values = Expression.Parameter(typeof(object?[]), "values");
            var arguments = method.GetParameters().Select((parameter, index)
                => Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(index)), parameter.ParameterType));
            Expression body = Expression.Call(Expression.Convert(target, method.DeclaringType!), method, arguments);
            if (method.ReturnType == typeof(void)) { body = Expression.Block(body, Expression.Constant(null, typeof(object))); }
            else
            {
                if (IsValueTask(method.ReturnType)) { body = Expression.Call(body, method.ReturnType.GetMethod(nameof(ValueTask.AsTask))!); }
                body = Expression.Convert(body, typeof(object));
            }
            return Expression.Lambda<Func<object, object?[], object?>>(body, target, values).Compile(preferInterpretation: !RuntimeFeature.IsDynamicCodeCompiled);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // A shape expression trees cannot call: reflect instead; Call unwraps the TargetInvocationException this raises.
            var asTask = IsValueTask(method.ReturnType) ? method.ReturnType.GetMethod(nameof(ValueTask.AsTask)) : null;
            return (target, values) =>
            {
                var returned = method.Invoke(target, values);
                return asTask is null ? returned : asTask.Invoke(returned, null);
            };
        }
    }

    private static async IAsyncEnumerable<object?> Items<T>(object source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in ((IAsyncEnumerable<T>)source).WithCancellation(cancellationToken).ConfigureAwait(false)) { yield return item; }
    }
}
