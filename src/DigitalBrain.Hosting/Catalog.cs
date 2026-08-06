using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain;

internal sealed class Catalog : ICatalog
{
    private readonly Dictionary<string, Type> neurons;
    private readonly Dictionary<string, Type> facts;
    private readonly Dictionary<Type, string> factKinds;
    private readonly Dictionary<Type, HashSet<string>> listeners;

    private Catalog(
        Dictionary<string, Type> neurons,
        Dictionary<string, Type> facts,
        Dictionary<Type, string> factKinds,
        Dictionary<Type, HashSet<string>> listeners)
    {
        this.neurons = neurons;
        this.facts = facts;
        this.factKinds = factKinds;
        this.listeners = listeners;
    }

    public IReadOnlyCollection<Type> FactTypes => factKinds.Keys;

    internal static Catalog Build(IReadOnlyList<Type> neuronTypes)
    {
        ArgumentNullException.ThrowIfNull(neuronTypes);
        var neurons = new Dictionary<string, Type>(StringComparer.Ordinal);
        var facts = new Dictionary<string, Type>(StringComparer.Ordinal);
        var factKinds = new Dictionary<Type, string>();
        var listeners = new Dictionary<Type, HashSet<string>>();
        RegisterFact(facts, factKinds, typeof(DeliveryFailed));
        foreach (var fact in SendableFactTypesOf(neuronTypes))
        {
            RequireFact(typeof(Catalog), fact);
            RegisterFact(facts, factKinds, fact);
        }

        foreach (var neuron in neuronTypes)
        {
            var kind = RequireNeuronKind(neuron);
            if (neurons.TryGetValue(kind, out var existing) && existing != neuron)
            {
                throw new InvalidOperationException(
                    $"Neuron kind '{kind}' is minted by both {Describe(existing)} and {Describe(neuron)}.");
            }

            neurons[kind] = neuron;
            foreach (var contract in neuron.GetInterfaces())
            {
                if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(INeuron<>))
                {
                    continue;
                }

                var fact = contract.GetGenericArguments()[0];
                RequireFact(neuron, fact);
                RegisterFact(facts, factKinds, fact);
                if (!listeners.TryGetValue(fact, out var kinds))
                {
                    kinds = new HashSet<string>(StringComparer.Ordinal);
                    listeners.Add(fact, kinds);
                }

                kinds.Add(kind);
            }
        }

        return new Catalog(neurons, facts, factKinds, listeners);
    }

    public bool TryGetFactType(string kind, [NotNullWhen(true)] out Type? factType)
        => facts.TryGetValue(kind, out factType);

    public string KindOfFact(Type factType)
        => factKinds.TryGetValue(factType, out var kind)
            ? kind
            : throw new InvalidOperationException($"{Describe(factType)} is not declared in the catalog.");

    public IReadOnlyCollection<string> ListenerKindsOf(Type factType)
        => listeners.TryGetValue(factType, out var kinds) ? kinds : [];

    public bool HasNeuronKind(string kind) => neurons.ContainsKey(kind);

    internal static string Describe(Type type)
        => type.FullName ?? type.Name;

    private static void RegisterFact(
        Dictionary<string, Type> facts,
        Dictionary<Type, string> factKinds,
        Type fact)
    {
        var kind = NeuronId.KindOf(fact);
        if (facts.TryGetValue(kind, out var existing) && existing != fact)
        {
            throw new InvalidOperationException(
                $"Fact kind '{kind}' is minted by both {Describe(existing)} and {Describe(fact)}.");
        }

        facts[kind] = fact;
        factKinds[fact] = kind;
    }

    private static void RequireFact(Type neuron, Type fact)
    {
        if (fact.IsAbstract || fact.IsGenericType || !fact.IsSealed)
        {
            throw new InvalidOperationException(
                $"{Describe(neuron)} must hear a sealed, concrete fact; got {Describe(fact)}.");
        }
    }

    private static IEnumerable<Type> SendableFactTypesOf(IReadOnlyList<Type> neuronTypes)
        => neuronTypes
            .Select(neuron => neuron.Assembly)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(fact => typeof(Synapse).IsAssignableFrom(fact)
                && !fact.IsAbstract
                && !fact.IsGenericType);

    private static string RequireNeuronKind(Type neuron)
    {
        var hasGrainType = neuron.CustomAttributes.Any(attribute
            => attribute.AttributeType.FullName == "Orleans.GrainTypeAttribute"
                && attribute.ConstructorArguments.Count == 1
                && attribute.ConstructorArguments[0].Value is string value
                && !string.IsNullOrWhiteSpace(value));
        if (!hasGrainType)
        {
            throw new InvalidOperationException(
                $"{Describe(neuron)} must declare [GrainType(\"stable-kind\")] for its durable NeuronId kind.");
        }

        return NeuronId.KindOf(neuron);
    }
}
