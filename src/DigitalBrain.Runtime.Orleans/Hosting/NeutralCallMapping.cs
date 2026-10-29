using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Invocation;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// How the host routes a bound call through its explicit interleaved or ordinary endpoint, using the class registered
/// by this host rather than process-wide type metadata:
/// <list type="bullet">
/// <item><see cref="InterleavesAttribute"/> and <see cref="ReadOnlyCallAttribute"/> on the contract or class method interleave fully
/// (a read-only call interleaves with every turn, as in TestOS).</item>
/// <item><see cref="InterleavesWhenAttribute"/>, inherited, names <c>static bool P(RuntimeInvocation)</c>; the nearest declaration wins.</item>
/// <item><see cref="ReentrantAttribute"/> classes are hosted by <c>neuron.host.reentrant</c>; the predicate also says yes for them.</item>
/// </list>
/// <see cref="OneWayCallAttribute"/> needs no mapping here: the proxy sends it through the one-way <c>Post</c>, scheduled like Invoke.
/// </summary>
internal static class NeutralCallMapping
{
    private static readonly ConcurrentDictionary<(Type Class, MethodInfo Method), bool> Methods = new();
    private static readonly ConcurrentDictionary<Type, Func<RuntimeInvocation, bool>?> Predicates = new();

    /// <summary>Whether <paramref name="call"/> may run while another turn of its neuron is suspended.</summary>
    public static bool MayInterleave(Type neuronClass, BoundCall bound, RuntimeInvocation call)
    {
        ArgumentNullException.ThrowIfNull(neuronClass);
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(call);
        if (neuronClass.IsDefined(typeof(ReentrantAttribute), inherit: true)) { return true; }
        if (Methods.GetOrAdd((neuronClass, bound.Method), static key => Declared(key.Class, key.Method))) { return true; }
        if (PredicateOf(neuronClass) is not { } predicate) { return false; }
        try { return predicate(call); }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }

    /// <summary>The class's <see cref="InterleavesWhenAttribute"/> predicate, or null when it declares none.</summary>
    /// <exception cref="InvalidOperationException">The named predicate is not a <c>static bool P(RuntimeInvocation)</c> on the class or a base.</exception>
    public static Func<RuntimeInvocation, bool>? PredicateOf(Type neuronClass)
    {
        ArgumentNullException.ThrowIfNull(neuronClass);
        return Predicates.GetOrAdd(neuronClass, Resolve);
    }

    private static Func<RuntimeInvocation, bool>? Resolve(Type neuronClass)
    {
        if (neuronClass.GetCustomAttribute<InterleavesWhenAttribute>(inherit: true) is not { } when) { return null; }
        for (var level = neuronClass; level is not null && level != typeof(object); level = level.BaseType)
        {
            var method = level.GetMethod(when.Predicate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, [typeof(RuntimeInvocation)]);
            if (method is null) { continue; }
            if (method.ReturnType != typeof(bool)) { break; }
            return method.CreateDelegate<Func<RuntimeInvocation, bool>>();
        }
        throw new InvalidOperationException($"{neuronClass.FullName} names [InterleavesWhen(\"{when.Predicate}\")] but has no static bool {when.Predicate}(RuntimeInvocation).");
    }

    private static bool Declared(Type neuronClass, MethodInfo contractMethod)
    {
        List<MethodInfo> methods = [contractMethod];
        if (contractMethod.DeclaringType is { IsInterface: true } contract && contract.IsAssignableFrom(neuronClass))
        {
            var map = neuronClass.GetInterfaceMap(contract);
            var index = Array.IndexOf(map.InterfaceMethods, contractMethod);
            if (index >= 0) { methods.Add(map.TargetMethods[index]); }
        }
        return methods.Any(method => method.IsDefined(typeof(InterleavesAttribute), false) || method.IsDefined(typeof(ReadOnlyCallAttribute), false));
    }
}
