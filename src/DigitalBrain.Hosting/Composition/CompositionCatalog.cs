using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain;

internal sealed class CompositionCatalog
{
    private readonly Dictionary<string, Type> behaviorTypes;
    private readonly Dictionary<string, Type> synapseTypes;
    private readonly Dictionary<Type, string> synapseKinds;
    private readonly Dictionary<Type, HashSet<string>> listeners;
    private readonly HashSet<Type> ingressSynapses;

    private CompositionCatalog(
        Dictionary<string, Type> behaviorTypes,
        Dictionary<string, Type> synapseTypes,
        Dictionary<Type, string> synapseKinds,
        Dictionary<Type, HashSet<string>> listeners,
        IReadOnlyList<WorkspaceServiceRegistration> workspaceServices,
        HashSet<Type> ingressSynapses,
        IReadOnlyList<Assembly> wireAssemblies)
    {
        this.behaviorTypes = behaviorTypes;
        this.synapseTypes = synapseTypes;
        this.synapseKinds = synapseKinds;
        this.listeners = listeners;
        this.ingressSynapses = ingressSynapses;
        WorkspaceServices = workspaceServices;
        WireAssemblies = wireAssemblies;
    }

    internal IReadOnlyCollection<Type> SynapseTypes => synapseKinds.Keys;

    internal IReadOnlyCollection<Type> BehaviorTypes => behaviorTypes.Values;

    internal IReadOnlyList<Assembly> WireAssemblies { get; }

    internal IReadOnlyList<WorkspaceServiceRegistration> WorkspaceServices { get; }

    internal static CompositionCatalog Create(
        IReadOnlyList<Assembly> vocabularyAssemblies,
        IReadOnlyList<NeuronRegistration> registrations,
        IReadOnlyList<WorkspaceServiceRegistration> workspaceServices,
        IReadOnlyCollection<Type> ingressSynapses)
    {
        ArgumentNullException.ThrowIfNull(vocabularyAssemblies);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(workspaceServices);
        ArgumentNullException.ThrowIfNull(ingressSynapses);

        var moduleAssemblies = vocabularyAssemblies
            .Concat(registrations.Select(static registration => registration.BehaviorType.Assembly))
            .Distinct()
            .ToArray();
        foreach (var assembly in moduleAssemblies)
        {
            ModuleAssemblyBoundary.Validate(assembly);
        }

        var behaviorTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        var synapseTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        var synapseKinds = new Dictionary<Type, string>();
        var listeners = new Dictionary<Type, HashSet<string>>();
        var registeredWorkspaceServices = new HashSet<Type>();

        foreach (var workspaceService in workspaceServices)
        {
            if (!registeredWorkspaceServices.Add(workspaceService.ServiceType))
            {
                throw new InvalidOperationException(
                    $"Workspace service '{Describe(workspaceService.ServiceType)}' is registered more than once.");
            }
        }

        RegisterSynapse(synapseTypes, synapseKinds, typeof(DeliveryFailed));
        foreach (var assembly in vocabularyAssemblies)
        {
            foreach (var synapse in SynapsesDeclaredBy(assembly))
            {
                RequireSynapse(assembly.GetName().Name ?? assembly.FullName ?? "<unknown>", synapse);
                RegisterSynapse(synapseTypes, synapseKinds, synapse);
            }
        }

        var registeredIngressSynapses = new HashSet<Type>();
        foreach (var ingressSynapse in ingressSynapses)
        {
            RequireSynapse("External ingress", ingressSynapse);
            if (ingressSynapse == typeof(DeliveryFailed))
            {
                throw new InvalidOperationException(
                    $"{nameof(DeliveryFailed)} is a Hosting-only terminal delivery outcome and cannot be external ingress.");
            }

            if (!synapseKinds.ContainsKey(ingressSynapse))
            {
                throw new InvalidOperationException(
                    $"External ingress {Describe(ingressSynapse)} is not registered vocabulary.");
            }

            registeredIngressSynapses.Add(ingressSynapse);
        }

        foreach (var registration in registrations)
        {
            RequireBehavior(registration.BehaviorType);
            if (string.Equals(registration.Kind, SynapseSourceIdentity.Kind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{SynapseSourceIdentity.Kind}' is reserved for external synapse sources.");
            }

            if (behaviorTypes.TryGetValue(registration.Kind, out var existing)
                && existing != registration.BehaviorType)
            {
                throw new InvalidOperationException(
                    $"Neuron kind '{registration.Kind}' is registered by both {Describe(existing)} and {Describe(registration.BehaviorType)}.");
            }

            behaviorTypes[registration.Kind] = registration.BehaviorType;
            foreach (var contract in registration.BehaviorType.GetInterfaces())
            {
                if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(INeuron<>))
                {
                    continue;
                }

                var synapse = contract.GetGenericArguments()[0];
                RequireSynapse(Describe(registration.BehaviorType), synapse);
                if (!synapseKinds.ContainsKey(synapse))
                {
                    throw new InvalidOperationException(
                        $"{Describe(registration.BehaviorType)} handles {Describe(synapse)}, "
                        + "but its assembly was not registered as vocabulary.");
                }

                if (!listeners.TryGetValue(synapse, out var kinds))
                {
                    kinds = new HashSet<string>(StringComparer.Ordinal);
                    listeners.Add(synapse, kinds);
                }

                kinds.Add(registration.Kind);
            }
        }

        var wireAssemblies = moduleAssemblies
            .Append(typeof(Neuron).Assembly)
            .Append(typeof(SynapseSource).Assembly)
            .Append(typeof(CompositionCatalog).Assembly)
            .Distinct()
            .ToArray();
        return new CompositionCatalog(
            behaviorTypes,
            synapseTypes,
            synapseKinds,
            listeners,
            [.. workspaceServices],
            registeredIngressSynapses,
            wireAssemblies);
    }

    internal bool TryGetSynapseType(string kind, [NotNullWhen(true)] out Type? synapseType)
        => synapseTypes.TryGetValue(kind, out synapseType);

    internal string KindOfSynapse(Type synapseType)
        => synapseKinds.TryGetValue(synapseType, out var kind)
            ? kind
            : throw new InvalidOperationException($"{Describe(synapseType)} is not registered vocabulary.");

    internal IReadOnlyCollection<string> ListenerKindsOf(Type synapseType)
        => listeners.TryGetValue(synapseType, out var kinds) ? kinds : [];

    internal bool AllowsIngress(Type synapseType) => ingressSynapses.Contains(synapseType);

    internal bool HasNeuronKind(string kind) => behaviorTypes.ContainsKey(kind);

    internal Type BehaviorTypeOf(string kind)
        => behaviorTypes.TryGetValue(kind, out var behavior)
            ? behavior
            : throw new InvalidOperationException($"Neuron kind '{kind}' is not registered.");

    internal Neuron CreateBehavior(string kind, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return (Neuron)ActivatorUtilities.CreateInstance(services, BehaviorTypeOf(kind));
    }

    internal static string Describe(Type type) => type.FullName ?? type.Name;

    internal static Type? StateTypeOf(Type behavior)
    {
        for (var current = behavior.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Neuron<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static IEnumerable<Type> SynapsesDeclaredBy(Assembly assembly)
        => assembly.GetTypes().Where(static type => typeof(Synapse).IsAssignableFrom(type)
            && !type.IsAbstract
            && !type.IsGenericType);

    private static void RegisterSynapse(
        Dictionary<string, Type> synapseTypes,
        Dictionary<Type, string> synapseKinds,
        Type synapse)
    {
        var kind = SynapseKinds.NameOf(synapse);
        if (synapseTypes.TryGetValue(kind, out var existing) && existing != synapse)
        {
            throw new InvalidOperationException(
                $"Synapse kind '{kind}' is declared by both {Describe(existing)} and {Describe(synapse)}.");
        }

        synapseTypes[kind] = synapse;
        synapseKinds[synapse] = kind;
    }

    private static void RequireBehavior(Type behavior)
    {
        if (!typeof(Neuron).IsAssignableFrom(behavior) || behavior.IsAbstract || behavior.IsGenericType)
        {
            throw new InvalidOperationException($"{Describe(behavior)} is not a concrete DigitalBrain behavior.");
        }
    }

    private static void RequireSynapse(string owner, Type synapse)
    {
        if (synapse.IsAbstract || synapse.IsGenericType || !synapse.IsSealed)
        {
            throw new InvalidOperationException(
                $"{owner} requires a sealed, concrete synapse; got {Describe(synapse)}.");
        }
    }
}
