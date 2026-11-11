using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Neurons;

namespace DigitalBrain.Kernel;

/// <summary>
/// Pre-compiled immutable registry of contract call descriptors built at contract registration or deployment.
/// Compiles delegates using expression trees for O(1) invocation.
/// </summary>
internal sealed class CallCatalog
{
    private static readonly MethodInfo ItemsMethod = typeof(CallCatalog).GetMethod(nameof(Items), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo CastTaskMethod = typeof(CallCatalog).GetMethod(nameof(CastTask), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo VoidTaskMethod = typeof(CallCatalog).GetMethod(nameof(VoidTask), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo CastValueTaskMethod = typeof(CallCatalog).GetMethod(nameof(CastValueTask), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo VoidValueTaskMethod = typeof(CallCatalog).GetMethod(nameof(VoidValueTask), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo FromResultMethod = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(object));

    private static readonly ConcurrentDictionary<Type, CallCatalog> ContractCache = new();
    private static readonly ConcurrentDictionary<Type, CallCatalog> NeuronCache = new();

    private readonly Dictionary<(string Method, int Arity), CallDescriptor[]> _byMethodAndArity;
    private readonly Dictionary<(string Contract, string Method, int Arity), CallDescriptor[]> _byContractMethodAndArity;
    private readonly List<CallDescriptor> _descriptors;

    private CallCatalog(Type targetType, IEnumerable<CallDescriptor> descriptors)
    {
        TargetType = targetType;
        _descriptors = descriptors.ToList();

        _byMethodAndArity = _descriptors
            .GroupBy(d => (d.MethodName, d.Arity))
            .ToDictionary(g => g.Key, g => g.ToArray());

        _byContractMethodAndArity = _descriptors
            .Where(d => d.Contract is not null)
            .GroupBy(d => (NeuronProxy.ContractName(d.Contract!), d.MethodName, d.Arity), StringComparerTuple.Instance)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>The contract interface or neuron class this catalog describes.</summary>
    public Type TargetType { get; }

    /// <summary>All descriptors in this catalog.</summary>
    public IReadOnlyList<CallDescriptor> Descriptors => _descriptors;

    /// <summary>Builds a new immutable <see cref="CallCatalog"/> for <paramref name="contractType"/>.</summary>
    public static CallCatalog Build(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        var descriptors = DescribeMethods(contractType, contractType);
        return new CallCatalog(contractType, descriptors);
    }

    /// <summary>Builds a new immutable <see cref="CallCatalog"/> for all contracts implemented by <paramref name="neuronClass"/>.</summary>
    public static CallCatalog BuildForNeuron(Type neuronClass)
    {
        ArgumentNullException.ThrowIfNull(neuronClass);
        var contracts = neuronClass.GetInterfaces();
        var descriptors = new List<CallDescriptor>();
        foreach (var contract in contracts)
        {
            if (contract == typeof(IDisposable) || contract == typeof(IAsyncDisposable)) { continue; }
            descriptors.AddRange(DescribeMethods(contract, neuronClass));
        }

        return new CallCatalog(neuronClass, descriptors);
    }

    /// <summary>Gets or builds a cached <see cref="CallCatalog"/> for <paramref name="contractType"/>.</summary>
    public static CallCatalog GetOrCreate(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        return contractType.IsCollectible ? Build(contractType) : ContractCache.GetOrAdd(contractType, Build);
    }

    /// <summary>Gets or builds a cached <see cref="CallCatalog"/> for <paramref name="neuronClass"/>.</summary>
    public static CallCatalog GetOrCreateForNeuron(Type neuronClass)
    {
        ArgumentNullException.ThrowIfNull(neuronClass);
        return neuronClass.IsCollectible ? BuildForNeuron(neuronClass) : NeuronCache.GetOrAdd(neuronClass, BuildForNeuron);
    }

    /// <summary>Tries to resolve a descriptor by method name and non-token arity.</summary>
    public bool TryResolve(string methodName, int arity, [NotNullWhen(true)] out CallDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        if (_byMethodAndArity.TryGetValue((methodName, arity), out var candidates) && candidates.Length > 0)
        {
            descriptor = candidates[0];
            return true;
        }

        descriptor = null;
        return false;
    }

    /// <summary>Tries to resolve a descriptor by contract name, method name and non-token arity.</summary>
    public bool TryResolve(string contract, string methodName, int arity, [NotNullWhen(true)] out CallDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(methodName);
        if (_byContractMethodAndArity.TryGetValue((contract, methodName, arity), out var candidates) && candidates.Length > 0)
        {
            descriptor = candidates[0];
            return true;
        }

        return TryResolve(methodName, arity, out descriptor);
    }

    /// <summary>Tries to resolve a descriptor by method name and JSON arguments (disambiguating overloads).</summary>
    public bool TryResolve(string methodName, JsonElement[] arguments, [NotNullWhen(true)] out CallDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_byMethodAndArity.TryGetValue((methodName, arguments.Length), out var candidates) || candidates.Length == 0)
        {
            descriptor = null;
            return false;
        }

        if (candidates.Length == 1)
        {
            descriptor = candidates[0];
            return true;
        }

        var matching = candidates.Where(c => Accepts(c, arguments)).ToArray();
        if (matching.Length == 1)
        {
            descriptor = matching[0];
            return true;
        }

        descriptor = null;
        return false;
    }

    /// <summary>Resolves a descriptor by method name and arity, throwing <see cref="BindingException"/> if not found.</summary>
    public CallDescriptor Resolve(string methodName, int arity)
    {
        if (TryResolve(methodName, arity, out var descriptor)) { return descriptor; }
        throw new BindingException($"{TargetType.Name} has no method {methodName} taking {arity} arguments.");
    }

    /// <summary>Resolves a descriptor by method name and JSON arguments, throwing <see cref="BindingException"/> if not found or ambiguous.</summary>
    public CallDescriptor Resolve(string methodName, JsonElement[] arguments)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_byMethodAndArity.TryGetValue((methodName, arguments.Length), out var candidates) || candidates.Length == 0)
        {
            throw new BindingException($"{TargetType.Name} has no method {methodName} taking {arguments.Length} arguments.");
        }

        if (candidates.Length == 1) { return candidates[0]; }

        var matching = candidates.Where(c => Accepts(c, arguments)).ToArray();
        return matching.Length switch
        {
            1 => matching[0],
            0 => throw new BindingException($"{TargetType.Name}.{methodName}: the arguments match no overload."),
            _ => throw new BindingException($"{TargetType.Name}.{methodName}: the arguments match more than one overload."),
        };
    }

    /// <summary>Returns all candidate descriptors for the method name and arity.</summary>
    public IReadOnlyList<CallDescriptor> GetCandidates(string methodName, int arity)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        return _byMethodAndArity.TryGetValue((methodName, arity), out var candidates) ? candidates : [];
    }

    private static bool Accepts(CallDescriptor candidate, JsonElement[] arguments)
    {
        var argument = 0;
        foreach (var type in candidate.ParameterTypes)
        {
            if (type == typeof(CancellationToken)) { continue; }
            if (argument >= arguments.Length) { return false; }
            try { _ = arguments[argument++].Deserialize(type, RuntimeProtocol.OptionsFor(type)); }
            catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException) { return false; }
        }

        return true;
    }

    private static List<CallDescriptor> DescribeMethods(Type contract, Type hostClass)
    {
        var reentrant = hostClass.IsDefined(typeof(ReentrantAttribute), inherit: true);
        var methods = EligibleMethods(contract);
        var descriptors = new List<CallDescriptor>();

        Dictionary<Type, InterfaceMapping>? interfaceMaps = null;

        foreach (var method in methods)
        {
            var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var arity = parameterTypes.Count(t => t != typeof(CancellationToken));

            MethodInfo? implementation = null;
            if (hostClass != contract && contract.IsInterface && contract.IsAssignableFrom(hostClass))
            {
                interfaceMaps ??= new Dictionary<Type, InterfaceMapping>();
                var declaring = method.DeclaringType ?? contract;
                if (!interfaceMaps.TryGetValue(declaring, out var map))
                {
                    interfaceMaps[declaring] = map = hostClass.GetInterfaceMap(declaring);
                }

                var index = Array.IndexOf(map.InterfaceMethods, method);
                if (index >= 0) { implementation = map.TargetMethods[index]; }
            }

            var readOnly = Marked<ReadOnlyCallAttribute>(method, implementation);
            var isInterleaved = reentrant || readOnly || Marked<InterleavesAttribute>(method, implementation);
            var hasInterleavesWhen = hostClass.IsDefined(typeof(InterleavesWhenAttribute), inherit: true);

            var scheduling = isInterleaved ? CallScheduling.Interleaved
                : hasInterleavesWhen ? CallScheduling.Conditional
                : CallScheduling.Turn;

            var access = ResolveAccess(contract, method, implementation);
            var timeout = method.GetCustomAttribute<CallTimeoutAttribute>()?.Timeout
                ?? implementation?.GetCustomAttribute<CallTimeoutAttribute>()?.Timeout;
            var isOneWay = method.IsDefined(typeof(OneWayCallAttribute), false)
                || (implementation?.IsDefined(typeof(OneWayCallAttribute), false) ?? false);

            var (kind, resultType, taskResultType, isTask, streamer) = AnalyzeReturnShape(method.ReturnType);
            var invoker = CompileInvoker(method);

            var descriptor = new CallDescriptor(method.Name, arity, method.ReturnType, invoker)
            {
                Contract = contract,
                Method = method,
                Scheduling = scheduling,
                Access = access,
                ReadOnly = readOnly,
                ParameterTypes = parameterTypes,
                Kind = kind,
                ResultType = resultType,
                TaskResultType = taskResultType,
                IsTask = isTask,
                Timeout = timeout,
                IsOneWay = isOneWay,
                Streamer = streamer,
            };

            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    private static bool Marked<TAttribute>(MethodInfo contractMethod, MethodInfo? implementationMethod) where TAttribute : Attribute
        => contractMethod.IsDefined(typeof(TAttribute), false)
        || (implementationMethod?.IsDefined(typeof(TAttribute), false) ?? false);

    private static OperationAccess ResolveAccess(Type contract, MethodInfo method, MethodInfo? implementation)
        => OperationAccessOf.Resolve(contract, method, implementation);

    private static (InvocationResultKind Kind, Type? ResultType, Type? TaskResultType, bool IsTask, Func<object, CancellationToken, IAsyncEnumerable<object?>>? Streamer) AnalyzeReturnShape(Type returnType)
    {
        var definition = returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : null;
        Type? resultType = null;
        Type? taskResultType = null;
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

        return (kind, resultType, taskResultType, isTask, streamer);
    }

    internal static Func<object, object?[], Task<object?>> CompileInvoker(MethodInfo method)
    {
        var targetParam = Expression.Parameter(typeof(object), "target");
        var argsParam = Expression.Parameter(typeof(object?[]), "args");
        var parameters = method.GetParameters();

        var targetCast = Expression.Convert(targetParam, method.DeclaringType!);
        var argExpressions = new Expression[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            if (paramType == typeof(CancellationToken))
            {
                var inBounds = Expression.GreaterThan(Expression.ArrayLength(argsParam), Expression.Constant(i));
                var fromArray = Expression.Convert(Expression.ArrayIndex(argsParam, Expression.Constant(i)), typeof(CancellationToken));
                argExpressions[i] = Expression.Condition(inBounds, fromArray, Expression.Constant(CancellationToken.None, typeof(CancellationToken)));
            }
            else
            {
                var arrayElem = Expression.ArrayIndex(argsParam, Expression.Constant(i));
                argExpressions[i] = Expression.Convert(arrayElem, paramType);
            }
        }

        Expression callExpr = Expression.Call(targetCast, method, argExpressions);
        Expression body;

        var returnType = method.ReturnType;
        if (returnType == typeof(void))
        {
            var completed = Expression.Constant(Task.FromResult<object?>(null), typeof(Task<object?>));
            body = Expression.Block(callExpr, completed);
        }
        else if (returnType == typeof(Task))
        {
            body = Expression.Call(VoidTaskMethod, callExpr);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var castMethod = CastTaskMethod.MakeGenericMethod(resultType);
            body = Expression.Call(castMethod, callExpr);
        }
        else if (returnType == typeof(ValueTask))
        {
            body = Expression.Call(VoidValueTaskMethod, callExpr);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var castMethod = CastValueTaskMethod.MakeGenericMethod(resultType);
            body = Expression.Call(castMethod, callExpr);
        }
        else
        {
            var boxed = Expression.Convert(callExpr, typeof(object));
            body = Expression.Call(FromResultMethod, boxed);
        }

        try
        {
            return Expression.Lambda<Func<object, object?[], Task<object?>>>(body, targetParam, argsParam).Compile();
        }
        catch (Exception)
        {
            // Fallback invoker
            return async (target, args) =>
            {
                var actualArgs = new object?[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType == typeof(CancellationToken))
                    {
                        actualArgs[i] = i < args.Length && args[i] is CancellationToken ct ? ct : CancellationToken.None;
                    }
                    else
                    {
                        actualArgs[i] = i < args.Length ? args[i] : null;
                    }
                }

                object? ret;
                try
                {
                    ret = method.Invoke(target, actualArgs);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }

                if (ret is Task task)
                {
                    await task.ConfigureAwait(false);
                    var taskType = task.GetType();
                    if (taskType.IsGenericType)
                    {
                        var prop = taskType.GetProperty("Result");
                        return prop?.GetValue(task);
                    }

                    return null;
                }

                if (ret is ValueTask vt)
                {
                    await vt.ConfigureAwait(false);
                    return null;
                }

                return ret;
            };
        }
    }

    private static IEnumerable<MethodInfo> EligibleMethods(Type contract)
    {
        IEnumerable<MethodInfo> methods = contract.IsInterface
            ? contract.GetMethods().Concat(contract.GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
            : contract.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        HashSet<MethodInfo> excluded = contract.IsInterface ? [] : contract.GetInterfaces()
            .Where(inherited => inherited == typeof(IDisposable) || inherited == typeof(IAsyncDisposable))
            .SelectMany(inherited => contract.GetInterfaceMap(inherited).TargetMethods)
            .ToHashSet();

        return methods.Where(method => !method.IsSpecialName && !method.IsGenericMethodDefinition && !method.IsStatic
            && method.DeclaringType != typeof(object)
            && method.DeclaringType != typeof(IDisposable)
            && method.DeclaringType != typeof(IAsyncDisposable)
            && !excluded.Contains(method)
            && method.GetParameters().All(parameter => !parameter.ParameterType.IsByRef))
            .DistinctBy(m => (m.Name, string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName))));
    }

    private static async Task<object?> CastTask<T>(Task<T> task)
    {
        var result = await task.ConfigureAwait(false);
        return (object?)result;
    }

    private static async Task<object?> VoidTask(Task task)
    {
        await task.ConfigureAwait(false);
        return null;
    }

    private static async Task<object?> CastValueTask<T>(ValueTask<T> task)
    {
        var result = await task.ConfigureAwait(false);
        return (object?)result;
    }

    private static async Task<object?> VoidValueTask(ValueTask task)
    {
        await task.ConfigureAwait(false);
        return null;
    }

    private static async IAsyncEnumerable<object?> Items<T>(object source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in ((IAsyncEnumerable<T>)source).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private sealed class StringComparerTuple : IEqualityComparer<(string, string, int)>
    {
        public static readonly StringComparerTuple Instance = new();

        public bool Equals((string, string, int) x, (string, string, int) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.Ordinal)
            && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal)
            && x.Item3 == y.Item3;

        public int GetHashCode((string, string, int) obj)
            => HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Item1), StringComparer.Ordinal.GetHashCode(obj.Item2), obj.Item3);
    }
}
