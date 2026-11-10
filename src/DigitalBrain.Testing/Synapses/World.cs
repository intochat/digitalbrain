using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Invocation;

namespace DigitalBrain.Testing.Synapses;

/// <summary>
/// A scripted world behind one or more slots: answers capability calls in order or from their arguments, and records every call made.
/// </summary>
public sealed class World
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Slot, string Operation), Func<JsonElement, WorldAnswer>> _scripts = new();
    private readonly List<WorldCall> _calls = [];

    /// <summary>Every call made, in order.</summary>
    public IReadOnlyList<WorldCall> Calls
    {
        get
        {
            lock (_gate) { return [.. _calls]; }
        }
    }

    /// <summary>Answers calls of <paramref name="operation"/> on <paramref name="slot"/> in order; the last answer repeats.</summary>
    /// <param name="slot">The slot name.</param>
    /// <param name="operation">The capability method name.</param>
    /// <param name="answers">The answers in order; the last repeats.</param>
    /// <returns>This world.</returns>
    /// <exception cref="ArgumentException">No answer is given.</exception>
    public World On(string slot, string operation, params WorldAnswer[] answers)
    {
        ArgumentException.ThrowIfNullOrEmpty(slot);
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.Length == 0) { throw new ArgumentException("At least one answer is required.", nameof(answers)); }

        var copy = answers.ToArray();
        var index = 0;
        lock (_gate)
        {
            _scripts[(slot, operation)] = _ =>
            {
                var current = index < copy.Length - 1 ? index++ : copy.Length - 1;
                return copy[current];
            };
        }

        return this;
    }

    /// <summary>Answers each call of <paramref name="operation"/> on <paramref name="slot"/> from its arguments (a JSON object keyed by parameter name).</summary>
    /// <param name="slot">The slot name.</param>
    /// <param name="operation">The capability method name.</param>
    /// <param name="answer">Produces an answer from the call's arguments.</param>
    /// <returns>This world.</returns>
    public World On(string slot, string operation, Func<JsonElement, WorldAnswer> answer)
    {
        ArgumentException.ThrowIfNullOrEmpty(slot);
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(answer);

        lock (_gate)
        {
            _scripts[(slot, operation)] = answer;
        }

        return this;
    }

    /// <summary>A fake of <typeparamref name="TContract"/> for <paramref name="slot"/> that answers from this world; bind it with <c>os.With&lt;TContract&gt;(name, fake)</c>.</summary>
    /// <typeparam name="TContract">The capability interface.</typeparam>
    /// <param name="os">The test OS.</param>
    /// <param name="slot">The slot name.</param>
    /// <returns>The fake implementation of <typeparamref name="TContract"/>.</returns>
    public TContract As<TContract>(TestOS os, string slot) where TContract : class
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentException.ThrowIfNullOrEmpty(slot);
        return WorldProxy.Create<TContract>(this, os, slot);
    }

    internal WorldAnswer Step(string slot, string operation, JsonElement arguments)
    {
        Func<JsonElement, WorldAnswer>? script;
        lock (_gate)
        {
            _calls.Add(new WorldCall(slot, operation, arguments));
            if (_scripts.TryGetValue((slot, operation), out script))
            {
                return script(arguments);
            }
        }

        throw new InvalidOperationException($"the world has no answer for {slot}.{operation}");
    }
}

/// <summary>
/// A scripted outcome for one capability call in a <see cref="World"/>.
/// </summary>
public abstract record WorldAnswer
{
    /// <summary>A successful answer whose <paramref name="Result"/> is deserialized to the capability method's return type.</summary>
    /// <param name="Result">The JSON result.</param>
    public sealed record Ok(JsonElement Result) : WorldAnswer;

    /// <summary>A failing answer: the capability throws <paramref name="Error"/>.</summary>
    /// <param name="Error">The exception the capability throws.</param>
    public sealed record Fail(Exception Error) : WorldAnswer;

    /// <summary>An <see cref="Ok"/> parsed from <paramref name="json"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The answer.</returns>
    public static WorldAnswer Json(string json) => new Ok(JsonElement.Parse(json));

    /// <summary>An <see cref="Ok"/> of a JSON string.</summary>
    /// <param name="text">The string value.</param>
    /// <returns>The answer.</returns>
    public static WorldAnswer Text(string text) => new Ok(JsonSerializer.SerializeToElement(text));

    /// <summary>A <see cref="Fail"/> with the <see cref="HttpRequestException"/> thrown for <paramref name="status"/>.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <returns>The answer.</returns>
    public static WorldAnswer Status(int status) =>
        new Fail(new HttpRequestException($"Response status code does not indicate success: {status}.", null, (HttpStatusCode)status));

    /// <summary>A <see cref="Fail"/> with a timeout <see cref="TaskCanceledException"/>.</summary>
    /// <returns>The answer.</returns>
    public static WorldAnswer TimedOut() => new Fail(new TaskCanceledException("The request timed out."));
}

/// <summary>
/// One capability call recorded by a <see cref="World"/>.
/// </summary>
/// <param name="Slot">The slot name.</param>
/// <param name="Operation">The capability method name.</param>
/// <param name="Arguments">The call arguments as a JSON object keyed by parameter name.</param>
public sealed record WorldCall(string Slot, string Operation, JsonElement Arguments);

/// <summary>DispatchProxy implementation for <see cref="World.As{TContract}"/>.</summary>
#pragma warning disable CA1852
internal class WorldProxy : DispatchProxy
#pragma warning restore CA1852
{
    private static readonly MethodInfo AnswerAsyncMethod = typeof(WorldProxy).GetMethod(nameof(AnswerAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private World _world = null!;
    private TestOS _os = null!;
    private string _slot = "";

    internal static TContract Create<TContract>(World world, TestOS os, string slot) where TContract : class
    {
        var proxy = Create<TContract, WorldProxy>();
        var instance = (WorldProxy)(object)proxy;
        instance._world = world;
        instance._os = os;
        instance._slot = slot;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.DeclaringType == typeof(object))
        {
            return targetMethod.Name switch
            {
                nameof(Equals) => ReferenceEquals(this, args?[0]),
                nameof(GetHashCode) => RuntimeHelpers.GetHashCode(this),
                nameof(ToString) => $"{_slot}:{targetMethod.DeclaringType.Name}",
                _ => throw new NotSupportedException(targetMethod.Name),
            };
        }

        _ = _os;
        var arguments = EncodeArguments(targetMethod.GetParameters(), args);
        var returnType = targetMethod.ReturnType;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            return AnswerAsyncMethod.MakeGenericMethod(resultType).Invoke(this, [targetMethod.Name, arguments]);
        }

        throw new NotSupportedException($"{targetMethod.DeclaringType?.Name}.{targetMethod.Name} must return Task<T>.");
    }

    private static JsonElement EncodeArguments(ParameterInfo[] parameters, object?[]? args)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            if (param.ParameterType == typeof(CancellationToken) || param.Name is null) { continue; }
            var value = args is not null && i < args.Length ? args[i] : null;
            var type = value?.GetType() ?? param.ParameterType;
            map[param.Name] = JsonSerializer.SerializeToElement(value, type, RuntimeProtocol.OptionsFor(type));
        }

        return JsonSerializer.SerializeToElement(map);
    }

    private Task<TResult> AnswerAsync<TResult>(string operation, JsonElement arguments)
    {
        WorldAnswer answer;
        try
        {
            answer = _world.Step(_slot, operation, arguments);
        }
        catch (Exception error)
        {
            return Task.FromException<TResult>(error);
        }

        return answer switch
        {
            WorldAnswer.Ok ok => Task.FromResult(ok.Result.Deserialize<TResult>(RuntimeProtocol.OptionsFor(typeof(TResult)))!),
            WorldAnswer.Fail fail => Task.FromException<TResult>(fail.Error),
            _ => Task.FromException<TResult>(new InvalidOperationException($"Unsupported world answer {answer.GetType().Name}.")),
        };
    }
}
