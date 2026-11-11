using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Invocation;

namespace DigitalBrain.Kernel;

/// <summary>
/// Strongly-typed descriptor containing method metadata and pre-compiled invocation delegate.
/// </summary>
/// <param name="MethodName">The contract method name.</param>
/// <param name="Arity">The non-token argument count.</param>
/// <param name="ReturnType">The method declared return type.</param>
/// <param name="Invoker">The pre-compiled invocation delegate executing the method in O(1) time.</param>
internal sealed record CallDescriptor(
    string MethodName,
    int Arity,
    Type ReturnType,
    Func<object, object?[], Task<object?>> Invoker)
{
    /// <summary>The interface contract declaring this method.</summary>
    public Type? Contract { get; init; }

    /// <summary>The reflected method information.</summary>
    public MethodInfo? Method { get; init; }

    /// <summary>How the call is scheduled.</summary>
    public CallScheduling Scheduling { get; init; } = CallScheduling.Turn;

    /// <summary>Access classification for caller admission.</summary>
    public OperationAccess Access { get; init; } = OperationAccess.Owner;

    /// <summary>Whether the method is read-only.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The parameter types in declaration order (including any CancellationToken).</summary>
    public Type[] ParameterTypes { get; init; } = [];

    /// <summary>Result kind (None, Value, Stream).</summary>
    public InvocationResultKind Kind { get; init; } = InvocationResultKind.None;

    /// <summary>Result type or stream item type, or null when the call returns nothing.</summary>
    public Type? ResultType { get; init; }

    /// <summary>The task result type after awaiting.</summary>
    public Type? TaskResultType { get; init; }

    /// <summary>Whether the return type is Task or ValueTask.</summary>
    public bool IsTask { get; init; }

    /// <summary>Response timeout override if declared.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Whether the call is one-way (fire and forget).</summary>
    public bool IsOneWay { get; init; }

    /// <summary>Stream item enumerator factory for IAsyncEnumerable results.</summary>
    public Func<object, CancellationToken, IAsyncEnumerable<object?>>? Streamer { get; init; }

    /// <summary>Calls the method on <paramref name="target"/> using JSON arguments, returning JSON result.</summary>
    public async Task<JsonElement?> InvokeAsync(object target, JsonElement[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Kind == InvocationResultKind.Stream)
        {
            throw new BindingException($"{Contract?.Name ?? "Contract"}.{MethodName} returns a stream; stream it.");
        }

        var values = PrepareArguments(arguments, cancellationToken);
        var returned = await Invoker(target, values).ConfigureAwait(true);
        return ResultType is null ? null : PooledJson.Encode(returned, ResultType);
    }

    /// <summary>Calls a streaming method on <paramref name="target"/> and yields its items as JSON.</summary>
    public IAsyncEnumerable<JsonElement> StreamAsync(object target, JsonElement[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Kind != InvocationResultKind.Stream || Streamer is null)
        {
            throw new BindingException($"{Contract?.Name ?? "Contract"}.{MethodName} does not return a stream.");
        }

        var values = PrepareArguments(arguments, cancellationToken);
        // Invoke target synchronously or via Invoker to obtain the stream object
        object? streamObject;
        if (Method is not null)
        {
            streamObject = Method.Invoke(target, values);
        }
        else
        {
            var task = Invoker(target, values);
            streamObject = task.GetAwaiter().GetResult();
        }

        if (streamObject is null)
        {
            throw new InvalidOperationException($"{Contract?.Name ?? "Contract"}.{MethodName} returned no stream.");
        }

        return EncodeStream(Streamer(streamObject, cancellationToken), ResultType!, cancellationToken);
    }

    private static async IAsyncEnumerable<JsonElement> EncodeStream(
        IAsyncEnumerable<object?> items,
        Type itemType,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in items.ConfigureAwait(true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return JsonSerializer.SerializeToElement(item, itemType, RuntimeProtocol.OptionsFor(item?.GetType() ?? itemType));
        }
    }

    private object?[] PrepareArguments(JsonElement[] arguments, CancellationToken cancellationToken)
    {
        if (arguments is null || arguments.Length != Arity)
        {
            throw new BindingException($"{Contract?.Name ?? "Contract"}.{MethodName} takes {Arity} arguments.");
        }

        var values = new object?[ParameterTypes.Length];
        for (int index = 0, argument = 0; index < values.Length; index++)
        {
            var type = ParameterTypes[index];
            if (type == typeof(CancellationToken))
            {
                values[index] = cancellationToken;
                continue;
            }

            try
            {
                values[index] = arguments[argument++].Deserialize(type, RuntimeProtocol.OptionsFor(type));
            }
            catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new BindingException($"{Contract?.Name ?? "Contract"}.{MethodName}: argument {argument - 1} is not a {type.Name}.", error);
            }

            if (values[index] is null && type.IsValueType && Nullable.GetUnderlyingType(type) is null)
            {
                throw new BindingException($"{Contract?.Name ?? "Contract"}.{MethodName}: argument {argument - 1} cannot be null.");
            }
        }

        return values;
    }
}
