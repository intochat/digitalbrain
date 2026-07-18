using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using DigitalBrain.Protocol;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Os.Infrastructure.Orleans;

public static class SynapseDispatch
{
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<Type, MethodInfo>> HandlerCache = new();
    private static readonly ConcurrentDictionary<Type, FrozenSet<Type>> HandledTypesCache = new();

    private static IReadOnlyList<(string Neuron, string Synapse)>? _manifest;
    private static MethodInfo? _preResolveMethod;
    private static MethodInfo? _preResolveInvokerMethod;

    // Aggregated across all assemblies that reference the source generator (Kernel, Awesome, Sdk, Tests after analyzer refs).
    // Populated from KnownContracts (preferred, carries IsHandle) or fallback to KnownHandlers.
    private static IReadOnlyList<(string Neuron, string Synapse, bool IsHandle)>? _contracts;

    // True once the source-generated dispatch manifest is loaded. Startup/tests can assert this so a
    // generator regression fails loudly instead of silently degrading every dispatch to reflection.
    public static bool ManifestAvailable => GetManifestIfAvailable() is { Count: > 0 };

    internal static FrozenSet<Type> HandledTypes(Type neuronType) =>
        HandledTypesCache.GetOrAdd(neuronType, t => Handlers(t).Keys.ToFrozenSet());

    internal static Task DispatchAsync(object host, ILogger logger, NeuronId self, Synapse synapse)
    {
        var nType = host.GetType();
        var sType = synapse.GetType();

        var inv = GetPreResolvedInvokerFromManifest(nType, sType);
        if (inv is not null)
            return inv(host, synapse, CancellationToken.None);

        var handlers = Handlers(nType);
        if (handlers.TryGetValue(sType, out var method))
            return (Task)method.Invoke(host, [synapse, CancellationToken.None])!;

        logger.LogWarning("{Neuron}: no handler for {Synapse}", self, sType.Name);
        return Task.CompletedTask;
    }

    private static FrozenDictionary<Type, MethodInfo> Handlers(Type neuronType) =>
        HandlerCache.GetOrAdd(neuronType, static t =>
        {
            var manifest = GetManifestIfAvailable();
            var known = manifest?.Where(x => x.Neuron == t.FullName).ToList();
            if (known is not null && known.Count > 0)
            {
                var map = new Dictionary<Type, MethodInfo>();
                foreach (var (_, syn) in known)
                {
                    var st = Type.GetType(syn);
                    if (st is null) continue;
                    var m = GetPreResolvedFromManifest(t, st);
                    if (m is null)
                    {
                        var iface = typeof(IHandle<>).MakeGenericType(st);
                        m = iface.GetMethod(nameof(IHandle<Synapse>.HandleAsync))!;
                    }
                    map[st] = m;
                }

                // Defensive union: add any IHandle<> the manifest missed so an incomplete manifest
                // can never silently drop a handler. Manifest entries keep their pre-resolved MethodInfo;
                // only genuinely absent types are added via reflection.
                MergeReflectionHandlers(t, map);

                var fd = FrozenDictionary.ToFrozenDictionary(map);
                HandledTypesCache[t] = FrozenSet.ToFrozenSet(fd.Keys);
                return fd;
            }

            var map2 = new Dictionary<Type, MethodInfo>();
            foreach (var i in t.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandle<>)))
            {
                var st = i.GetGenericArguments()[0];
                var m = i.GetMethod(nameof(IHandle<Synapse>.HandleAsync))!;
                map2[st] = m;
            }
            var fd2 = FrozenDictionary.ToFrozenDictionary(map2);
            HandledTypesCache[t] = FrozenSet.ToFrozenSet(fd2.Keys);
            return fd2;
        });

    // Adds IHandle<TSynapse> interfaces present on neuronType but absent from map.
    // Manifest entries (which may carry pre-resolved MethodInfo) are never overwritten.
    internal static void MergeReflectionHandlers(Type neuronType, Dictionary<Type, MethodInfo> map)
    {
        foreach (var iface in neuronType.GetInterfaces()
                     .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandle<>)))
        {
            var synapseType = iface.GetGenericArguments()[0];
            if (!map.ContainsKey(synapseType))
                map[synapseType] = iface.GetMethod(nameof(IHandle<Synapse>.HandleAsync))!;
        }
    }

    private static IReadOnlyList<(string Neuron, string Synapse)>? GetManifestIfAvailable()
    {
        if (_manifest is not null) return _manifest;
        var list = new List<(string Neuron, string Synapse)>();
        var seen = new HashSet<(string, string)>();

        // Robust lookup: explicit check on likely assemblies (entry/executing + the Core one) first,
        // then full AppDomain scan. This survives certain ALC / late-load / Aspire child process scenarios
        // where a general GetAssemblies() enumeration may miss the assembly that was compiled against the generator.
        var candidates = new System.Collections.Generic.List<System.Reflection.Assembly>();
        var entry = System.Reflection.Assembly.GetEntryAssembly();
        if (entry != null) candidates.Add(entry);
        var exec = System.Reflection.Assembly.GetExecutingAssembly();
        if (exec != null) candidates.Add(exec);
        candidates.Add(typeof(SynapseDispatch).Assembly);

        foreach (var ca in candidates.Distinct())
        {
            TryExtractFromAssembly(ca, seen, list);
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            TryExtractFromAssembly(asm, seen, list);
        }

        _manifest = list.AsReadOnly();
        return _manifest;

        static void TryExtractFromAssembly(System.Reflection.Assembly asm, HashSet<(string, string)> seen, List<(string, string)> list)
        {
            var t = asm.GetType("DigitalBrain.SourceGen.DispatchManifest");
            if (t is null) return;
            var prop = t.GetProperty("KnownHandlers");
            if (prop?.GetValue(null) is (string, string)[] arr)
            {
                foreach (var x in arr)
                {
                    if (seen.Add(x))
                    {
                        list.Add((x.Item1, x.Item2));
                    }
                }
            }
        }
    }

    private static IReadOnlyList<(string Neuron, string Synapse, bool IsHandle)> GetAllKnownContracts()
    {
        if (_contracts is not null) return _contracts;

        var collected = new List<(string Neuron, string Synapse, bool IsHandle)>();
        var seen = new HashSet<(string, string, bool)>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("DigitalBrain.SourceGen.DispatchManifest");
            if (t is null) continue;

            // Prefer KnownContracts (includes IsHandle flag + both handle/emit per generator design).
            var contractsProp = t.GetProperty("KnownContracts");
            if (contractsProp?.GetValue(null) is (string, string, bool)[] contractsArr)
            {
                foreach (var entry in contractsArr)
                {
                    if (seen.Add(entry))
                        collected.Add(entry);
                }
                // Also capture pre-resolve hooks from first manifest that provides them (same as before).
                if (_preResolveMethod is null)
                {
                    _preResolveMethod = t.GetMethod("GetPreResolvedHandleMethod", BindingFlags.Public | BindingFlags.Static);
                    _preResolveInvokerMethod = t.GetMethod("GetPreResolvedInvoker", BindingFlags.Public | BindingFlags.Static);
                }
                continue;
            }

            // Fallback for assemblies with only KnownHandlers (pre-KnownContracts generator or minimal).
            var handlersProp = t.GetProperty("KnownHandlers");
            if (handlersProp?.GetValue(null) is (string, string)[] handlersArr)
            {
                foreach (var (n, s) in handlersArr)
                {
                    var e = (n, s, true);
                    if (seen.Add(e))
                        collected.Add(e);
                }
                if (_preResolveMethod is null)
                {
                    _preResolveMethod = t.GetMethod("GetPreResolvedHandleMethod", BindingFlags.Public | BindingFlags.Static);
                    _preResolveInvokerMethod = t.GetMethod("GetPreResolvedInvoker", BindingFlags.Public | BindingFlags.Static);
                }
            }
        }

        _contracts = collected.AsReadOnly();
        return _contracts;
    }

    public static int GetStaticHandlerCountFor(string synapseTypeName)
    {
        if (string.IsNullOrWhiteSpace(synapseTypeName)) return 0;

        var contracts = GetAllKnownContracts();
        if (contracts.Count == 0) return 0;

        var suffix = "." + synapseTypeName;
        var matchingNeurons = new HashSet<string>();

        foreach (var (neuron, syn, isHandle) in contracts)
        {
            if (!isHandle) continue;
            if (syn == synapseTypeName || syn.EndsWith(suffix))
                matchingNeurons.Add(neuron);
        }

        return matchingNeurons.Count;
    }

    private static MethodInfo? GetPreResolvedFromManifest(Type neuronType, Type synapseType)
    {
        try
        {
            var t = neuronType.Assembly.GetType("DigitalBrain.SourceGen.DispatchManifest");
            var method = t?.GetMethod("GetPreResolvedHandleMethod", BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, [neuronType, synapseType]) as MethodInfo;
        }
        catch { return null; }
    }

    private static Func<object, Synapse, CancellationToken, Task>? GetPreResolvedInvokerFromManifest(Type neuronType, Type synapseType)
    {
        try
        {
            var t = neuronType.Assembly.GetType("DigitalBrain.SourceGen.DispatchManifest");
            var method = t?.GetMethod("GetPreResolvedInvoker", BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, [neuronType, synapseType]) as Func<object, Synapse, CancellationToken, Task>;
        }
        catch { return null; }
    }
}