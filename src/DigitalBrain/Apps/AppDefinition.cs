using System.Collections.ObjectModel;
using System.Reflection;
using DigitalBrain.Capabilities;

namespace DigitalBrain;

/// <summary>Validated app and operation metadata for trusted app code. Does not construct an app.</summary>
public sealed partial class AppDefinition
{
    /// <summary>Discovers a concrete, closed App class marked with AppAttribute.</summary>
    public AppDefinition(Type appType)
    {
        ArgumentNullException.ThrowIfNull(appType);
        if (!appType.IsClass || appType.IsAbstract || appType.ContainsGenericParameters || !appType.IsSubclassOf(typeof(App)))
        {
            throw new InvalidOperationException($"{appType.FullName} must be a concrete, closed App class.");
        }
        var attribute = appType.GetCustomAttribute<AppAttribute>(false)
            ?? throw new InvalidOperationException($"{appType.FullName} has no [App(id)] attribute.");
        AppType = appType;
        var operations = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        var subscriptions = new List<AppSignalSubscription>();
        foreach (var method in appType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.ToString(), StringComparer.Ordinal))
        {
            if (method.GetCustomAttribute<OnAttribute>(false) is not { } onAttr) { continue; }
            var parameters = method.GetParameters();
            var tokens = parameters.Count(p => p.ParameterType == typeof(CancellationToken));
            if (method.IsStatic || method.IsGenericMethodDefinition || parameters.Any(p => p.ParameterType.IsByRef || p.ParameterType.IsPointer || p.ParameterType.IsByRefLike)
                || parameters.Length - tokens > 1 || tokens > 1 || (tokens == 1 && parameters[^1].ParameterType != typeof(CancellationToken))
                || (method.ReturnType != typeof(void) && !typeof(Task).IsAssignableFrom(method.ReturnType) && method.ReturnType != typeof(ValueTask)))
            {
                throw new InvalidOperationException($"{appType.FullName}.{method.Name} has an invalid [On] operation shape; use an instance, non-generic void, Task or ValueTask method with at most one input and an optional trailing CancellationToken.");
            }
            if (!operations.TryAdd(Key(method.Name), method))
            {
                throw new InvalidOperationException($"{appType.FullName} has duplicate operation alias '{method.Name}'.");
            }
            if (!string.IsNullOrWhiteSpace(onAttr.Source)
                && parameters.FirstOrDefault(p => p.ParameterType != typeof(CancellationToken)) is { } signalParam
                && typeof(Signal).IsAssignableFrom(signalParam.ParameterType))
            {
                subscriptions.Add(new AppSignalSubscription(method.Name, method, signalParam.ParameterType, onAttr.Source, onAttr.Contract));
            }
        }
        Operations = new ReadOnlyDictionary<string, MethodInfo>(operations);
        SignalSubscriptions = subscriptions.AsReadOnly();
        Id = attribute.Id;
        Name = appType.Name;
        Version = appType.Assembly.GetName().Version?.ToString() ?? "0.0.0";
        Accent = attribute.Accent;
        Category = attribute.Category;

        var requires = appType.GetConstructors().SelectMany(c => c.GetParameters())
            .Select(p => Contract(p.ParameterType)).OfType<Type>()
            .Select(t => t.GetCustomAttribute<CapabilityAttribute>(false)!.Id).Distinct().ToList();
        Requires = new AppRequirements(requires);

        Neurons = appType.Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true }
            && t.Namespace == appType.Namespace && typeof(Neuron).IsAssignableFrom(t) && t.IsDefined(typeof(NeuronAttribute), false)).ToList();
    }

    /// <summary>The app class this definition describes.</summary>
    public Type AppType { get; }
    /// <summary>The frozen app id.</summary>
    public string Id { get; }
    /// <summary>The display name.</summary>
    public string Name { get; }
    /// <summary>The app version.</summary>
    public string Version { get; }
    /// <summary>Optional hex accent color (<c>#RRGGBB</c>) for the app's squircle icon badge.</summary>
    public string? Accent { get; }
    /// <summary>Optional app category label (e.g., <c>Intelligence</c>, <c>Workspace</c>, <c>Studio</c>).</summary>
    public string? Category { get; }
    /// <summary>The neuron classes the app brings.</summary>
    public IReadOnlyList<Type> Neurons { get; }
    /// <summary>What the app requires from the brain.</summary>
    public AppRequirements Requires { get; }
    /// <summary>Operations keyed by lowercase method name with dashes removed. MethodInfo exposes dispatch parameter metadata.</summary>
    public IReadOnlyDictionary<string, MethodInfo> Operations { get; }
    /// <summary>Background neuron signal subscriptions declared via <c>[On("sourceKey")]</c>.</summary>
    public IReadOnlyList<AppSignalSubscription> SignalSubscriptions { get; }
    /// <summary>Finds an operation ignoring case and dashes; returns null when absent.</summary>
    public MethodInfo? FindOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return Operations.GetValueOrDefault(Key(operation));
    }

    private static string Key(string name) => name.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static Type? Contract(Type type)
    {
        if (type.IsInterface && type.IsDefined(typeof(CapabilityAttribute), false)) { return type; }
        if (type.IsArray) { return ContractElement(type.GetElementType()!); }
        if (!type.IsGenericType) { return null; }
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)
            ? ContractElement(type.GetGenericArguments()[0]) : null;
    }

    private static Type? ContractElement(Type type) => type.IsInterface && type.IsDefined(typeof(CapabilityAttribute), false) ? type : null;
}

/// <summary>Describes a background neuron signal subscription declared on an <see cref="App"/> reaction method.</summary>
public sealed record AppSignalSubscription(
    string OperationName,
    MethodInfo Method,
    Type SignalType,
    string SourceKey,
    Type? ContractType = null);
