using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Invocation;

namespace DigitalBrain.Testing;

// One neuron's turn: a call takes it unless it interleaves. Work always leaves the caller's synchronous stack first,
// so a queued call never runs inside the caller's turn.
internal sealed class TurnScheduler : IDisposable
{
    private readonly SemaphoreSlim _turn = new(1, 1);

    public async Task<T> Run<T>(bool interleave, Func<Task<T>> body)
    {
        await Task.Yield();
        if (interleave) { return await body().ConfigureAwait(false); }
        await _turn.WaitAsync().ConfigureAwait(false);
        try { return await body().ConfigureAwait(false); }
        finally { _turn.Release(); }
    }

    public void Dispose() => _turn.Dispose();
}

// The scheduling a call declares through the neutral attributes, the same rules the host runtime maps onto its actor scheduler:
// [Interleaves] and [ReadOnlyCall] on the contract or class method interleave; a [Reentrant] class (inherited) interleaves
// everything; an [InterleavesWhen] class (inherited) asks its static predicate bool P(RuntimeInvocation) per call.
internal static class CallMarkers
{
    private static readonly ConcurrentDictionary<(Type Class, MethodInfo Method), (bool Interleaves, bool OneWay)> Methods = new();
    private static readonly ConcurrentDictionary<Type, Func<RuntimeInvocation, bool>?> Predicates = new();

    public static (bool Interleaves, bool OneWay) Of(Type neuronClass, MethodInfo contractMethod, RuntimeInvocation call)
    {
        var declared = Methods.GetOrAdd((neuronClass, contractMethod), static key => Read(key.Class, key.Method));
        if (declared.Interleaves) { return declared; }
        if (neuronClass.IsDefined(typeof(ReentrantAttribute), inherit: true)) { return declared with { Interleaves = true }; }
        if (Predicates.GetOrAdd(neuronClass, Predicate) is { } predicate && predicate(call)) { return declared with { Interleaves = true }; }
        return declared;
    }

    public static Func<RuntimeInvocation, bool>? Predicate(Type neuronClass)
    {
        if (neuronClass.GetCustomAttribute<InterleavesWhenAttribute>(inherit: true) is not { } when) { return null; }
        var method = neuronClass.GetMethod(when.Predicate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(RuntimeInvocation)]);
        if (method is null || method.ReturnType != typeof(bool))
        {
            throw new InvalidOperationException($"{neuronClass.FullName} names [InterleavesWhen(\"{when.Predicate}\")] but has no static bool {when.Predicate}(RuntimeInvocation).");
        }
        return method.CreateDelegate<Func<RuntimeInvocation, bool>>();
    }

    private static (bool, bool) Read(Type neuronClass, MethodInfo contractMethod)
    {
        var methods = new List<MethodInfo> { contractMethod };
        if (contractMethod.DeclaringType is { IsInterface: true } contract && contract.IsAssignableFrom(neuronClass))
        {
            var map = neuronClass.GetInterfaceMap(contract);
            var index = Array.IndexOf(map.InterfaceMethods, contractMethod);
            if (index >= 0) { methods.Add(map.TargetMethods[index]); }
        }
        var interleaves = methods.Any(method => method.IsDefined(typeof(InterleavesAttribute), false) || method.IsDefined(typeof(ReadOnlyCallAttribute), false));
        var oneWay = methods.Any(method => method.IsDefined(typeof(OneWayCallAttribute), false));
        return (interleaves, oneWay);
    }
}
