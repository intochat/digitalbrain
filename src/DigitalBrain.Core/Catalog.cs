using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain;

// The boot catalog: one reflection pass over the composition's explicit neuron type set.
// A pure function of the types, held per-silo in DI, never static — test clusters compose
// independently. Every refusal below is a tested contract: the message is the assertion.
internal sealed class Catalog
{
    private static readonly Type[] ReservedKinds =
        [typeof(Connect), typeof(Disconnect), typeof(Schedule), typeof(Unschedule)];

    // Core's own journaled vocabulary is seeded so its kinds exist without any declaration
    // (Core journals Connect receptions and emits DeliveryFailed whether or not a module
    // listens) and so a module record reusing one of these names collides loudly.
    private static readonly Type[] CoreVocabulary =
    [
        typeof(Connect), typeof(Disconnect), typeof(ConnectionRefused), typeof(DeliveryFailed),
        typeof(AskExpired), typeof(Schedule), typeof(Unschedule), typeof(ScheduleFailed),
        typeof(DeclaredRouteSurvives),
    ];

    private readonly Dictionary<string, Type> kinds;
    private readonly Dictionary<string, Type> factKinds;
    private readonly Dictionary<Type, string> factKindNames;
    private readonly Dictionary<Type, HashSet<string>> listeners;
    private readonly Dictionary<Type, string> answerers;
    private readonly HashSet<(string NeuronKind, Type Question)> continuations;
    private readonly Dictionary<Type, string> shapeFingerprints;

    private Catalog(
        Dictionary<string, Type> kinds,
        Dictionary<string, Type> factKinds,
        Dictionary<Type, string> factKindNames,
        Dictionary<Type, HashSet<string>> listeners,
        Dictionary<Type, string> answerers,
        HashSet<(string NeuronKind, Type Question)> continuations,
        Dictionary<Type, string> shapeFingerprints,
        string fingerprint)
    {
        this.kinds = kinds;
        this.factKinds = factKinds;
        this.factKindNames = factKindNames;
        this.listeners = listeners;
        this.answerers = answerers;
        this.continuations = continuations;
        this.shapeFingerprints = shapeFingerprints;
        Fingerprint = fingerprint;
    }

    // Hash of the sorted (kind, declaredFactKind|answeredFactKind) rows; silos whose
    // fingerprints differ refuse to form a cluster.
    internal string Fingerprint { get; }

    internal IReadOnlyCollection<Type> FactTypes => factKindNames.Keys;

    internal static Catalog Build(IReadOnlyList<Type> neuronTypes)
    {
        ArgumentNullException.ThrowIfNull(neuronTypes);

        var kinds = new Dictionary<string, Type>(StringComparer.Ordinal);
        var factKinds = new Dictionary<string, Type>(StringComparer.Ordinal);
        var factKindNames = new Dictionary<Type, string>();
        var listeners = new Dictionary<Type, HashSet<string>>();
        var answerers = new Dictionary<Type, string>();
        var continuations = new HashSet<(string NeuronKind, Type Question)>();
        var rows = new List<string>();

        foreach (var coreFact in CoreVocabulary)
        {
            RegisterFactKind(factKinds, factKindNames, coreFact);
        }

        foreach (var neuronType in neuronTypes)
        {
            var kind = NeuronId.KindOf(neuronType);
            if (kinds.TryGetValue(kind, out var collidingNeuron))
            {
                if (collidingNeuron == neuronType)
                {
                    continue;   // the same type listed twice is not a collision
                }

                throw new InvalidOperationException(
                    $"Neuron kind '{kind}' is minted by both {Describe(collidingNeuron)} and {Describe(neuronType)}; "
                    + "kinds are lowercased class names and must be unique across the composition.");
            }

            kinds.Add(kind, neuronType);

            var heardFacts = new HashSet<Type>();
            var answeredQuestions = new HashSet<Type>();

            foreach (var declaration in neuronType.GetInterfaces())
            {
                if (!declaration.IsGenericType)
                {
                    continue;
                }

                var shape = declaration.GetGenericTypeDefinition();
                if (shape == typeof(INeuron<>))
                {
                    var factType = declaration.GetGenericArguments()[0];
                    if (factType.IsGenericType && factType.GetGenericTypeDefinition() == typeof(Answer<,>))
                    {
                        // Answer<Q,R> is Core-internal, never cataloged as a fact: the
                        // declaration is the continuation claim the Ask guard reads.
                        var questionType = factType.GetGenericArguments()[0];
                        var replyType = factType.GetGenericArguments()[1];
                        RequireCatalogable(neuronType, questionType);
                        RequireCatalogable(neuronType, replyType);
                        RegisterFactKind(factKinds, factKindNames, questionType);
                        RegisterFactKind(factKinds, factKindNames, replyType);
                        continuations.Add((kind, questionType));
                        rows.Add($"{kind} continues {NeuronId.KindOf(questionType)}");
                        continue;
                    }

                    RefuseReserved(neuronType, factType);
                    RequireCatalogable(neuronType, factType);
                    RegisterFactKind(factKinds, factKindNames, factType);
                    if (!listeners.TryGetValue(factType, out var hearingKinds))
                    {
                        hearingKinds = new HashSet<string>(StringComparer.Ordinal);
                        listeners.Add(factType, hearingKinds);
                    }

                    hearingKinds.Add(kind);
                    heardFacts.Add(factType);
                    rows.Add($"{kind} hears {NeuronId.KindOf(factType)}");
                }
                else if (shape == typeof(INeuron<,>))
                {
                    var questionType = declaration.GetGenericArguments()[0];
                    var replyType = declaration.GetGenericArguments()[1];
                    RequireCatalogable(neuronType, questionType);
                    RequireCatalogable(neuronType, replyType);
                    RegisterFactKind(factKinds, factKindNames, questionType);
                    RegisterFactKind(factKinds, factKindNames, replyType);
                    if (answerers.TryGetValue(questionType, out var collidingAnswerer))
                    {
                        throw new InvalidOperationException(
                            $"Question {Describe(questionType)} is answered by both '{collidingAnswerer}' and '{kind}'; "
                            + "a composition allows exactly one answerer kind per question.");
                    }

                    answerers.Add(questionType, kind);
                    RequireAnswererOverride(neuronType, declaration, questionType, replyType);
                    answeredQuestions.Add(questionType);
                    rows.Add($"{kind} answers {NeuronId.KindOf(questionType)}");
                }
            }

            foreach (var questionType in answeredQuestions)
            {
                if (heardFacts.Contains(questionType))
                {
                    throw new InvalidOperationException(
                        $"{Describe(neuronType)} declares both INeuron<{questionType.Name}> and "
                        + $"INeuron<{questionType.Name}, {ReplyTypeOf(questionType).Name}> for one question; "
                        + "a kind is either a listener or the answerer, never both.");
                }
            }
        }

        foreach (var (neuronKind, questionType) in continuations)
        {
            if (!answerers.ContainsKey(questionType))
            {
                throw new InvalidOperationException(
                    $"'{neuronKind}' declares INeuron<Answer<{questionType.Name}, {ReplyTypeOf(questionType).Name}>> "
                    + $"but no kind in the composition answers {Describe(questionType)}.");
            }
        }

        var payload = string.Join('\n', rows.Order(StringComparer.Ordinal));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var shapeFingerprints = factKindNames.Keys.ToDictionary(
            factType => factType,
            factType => FingerprintOfShape(factType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)));

        return new Catalog(
            kinds, factKinds, factKindNames, listeners, answerers, continuations, shapeFingerprints, fingerprint);
    }

    internal bool TryGetNeuronType(string kind, [NotNullWhen(true)] out Type? neuronType)
        => kinds.TryGetValue(kind, out neuronType);

    internal bool TryGetFactType(string kind, [NotNullWhen(true)] out Type? factType)
        => factKinds.TryGetValue(kind, out factType);

    internal string KindOfFact(Type factType)
    {
        ArgumentNullException.ThrowIfNull(factType);

        return factKindNames.TryGetValue(factType, out var kind)
            ? kind
            : throw new InvalidOperationException(
                $"{Describe(factType)} is not in the fact catalog; only declared synapse vocabulary can be journaled.");
    }

    internal IReadOnlyCollection<string> ListenerKindsOf(Type factType)
        => listeners.TryGetValue(factType, out var hearingKinds) ? hearingKinds : [];

    internal bool TryGetAnswererKind(Type questionType, [NotNullWhen(true)] out string? answererKind)
        => answerers.TryGetValue(questionType, out answererKind);

    internal bool IsAnswerer(string neuronKind, Type questionType)
        => answerers.TryGetValue(questionType, out var answererKind)
        && string.Equals(answererKind, neuronKind, StringComparison.Ordinal);

    internal bool HasContinuation(string neuronKind, Type questionType)
        => continuations.Contains((neuronKind, questionType));

    internal bool HasContinuation(Type neuronType, Type questionType)
        => HasContinuation(NeuronId.KindOf(neuronType), questionType);

    // The Answer-reconstruction guard's third conjunct (§5): a hash of the question's
    // serialized member names, computed at boot per fact type and compared against the
    // journaled ask body's property names before any Answer<Q,R> is dispatched — a drifted
    // shape must never rehydrate with silently-defaulted members.
    internal string ShapeFingerprintOf(Type factType) => shapeFingerprints[factType];

    internal static string FingerprintOfShape(IEnumerable<string> memberNames)
    {
        ArgumentNullException.ThrowIfNull(memberNames);

        var payload = string.Join('\n', memberNames
            .Select(JsonNamingPolicy.CamelCase.ConvertName)
            .Order(StringComparer.Ordinal));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    internal static Type? ReplyTypeOrNull(Type factType)
    {
        ArgumentNullException.ThrowIfNull(factType);

        for (var ancestor = factType.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (ancestor.IsGenericType && ancestor.GetGenericTypeDefinition() == typeof(Synapse<>))
            {
                return ancestor.GetGenericArguments()[0];
            }
        }

        return null;
    }

    internal static Type ReplyTypeOf(Type questionType)
        => ReplyTypeOrNull(questionType)
            ?? throw new InvalidOperationException(
                $"{Describe(questionType)} does not derive Synapse<TReply>; only questions carry a reply type.");

    internal static string Describe(Type type)
        => type.IsGenericType
            ? $"{(type.FullName ?? type.Name).Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>"
            : type.FullName ?? type.Name;

    private static void RegisterFactKind(
        Dictionary<string, Type> factKinds, Dictionary<Type, string> factKindNames, Type factType)
    {
        var kind = NeuronId.KindOf(factType);
        if (factKinds.TryGetValue(kind, out var collidingFact))
        {
            if (collidingFact != factType)
            {
                throw new InvalidOperationException(
                    $"Fact kind '{kind}' is minted by both {Describe(collidingFact)} and {Describe(factType)}; "
                    + "fact kinds are lowercased record names and must be unique across the composition.");
            }

            return;
        }

        factKinds.Add(kind, factType);
        factKindNames.Add(factType, kind);
    }

    private static void RefuseReserved(Type neuronType, Type factType)
    {
        if (Array.IndexOf(ReservedKinds, factType) >= 0)
        {
            throw new InvalidOperationException(
                $"{Describe(neuronType)} declares INeuron<{factType.Name}>, but '{NeuronId.KindOf(factType)}' "
                + "is a reserved Core kind handled on the receiving emitter.");
        }
    }

    private static void RequireCatalogable(Type neuronType, Type factType)
    {
        if (factType.IsAbstract)
        {
            throw new InvalidOperationException(
                $"{Describe(neuronType)} declares INeuron over abstract synapse {Describe(factType)}; "
                + "dispatch is exact-declared-type — declare sealed concrete facts "
                + "(journal-mirrors read journals instead of declaring wildcards).");
        }

        if (factType.IsGenericTypeDefinition || factType.IsGenericType)
        {
            throw new InvalidOperationException(
                $"{Describe(neuronType)} declares generic synapse type {Describe(factType)}; "
                + "fact kinds are minted from plain record names and a generic synapse cannot mint one.");
        }

        if (!factType.IsSealed)
        {
            throw new InvalidOperationException(
                $"Fact {Describe(factType)} declared by {Describe(neuronType)} is not sealed; "
                + "every concrete fact record must be sealed so exact-type dispatch is total.");
        }
    }

    private static void RequireAnswererOverride(
        Type neuronType, Type answererInterface, Type questionType, Type replyType)
    {
        // A target method whose DeclaringType is the interface itself is the default
        // implementation: nothing on the class overrode it.
        var map = neuronType.GetInterfaceMap(answererInterface);
        foreach (var target in map.TargetMethods)
        {
            if (target.DeclaringType is { IsInterface: false })
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"{Describe(neuronType)} declares INeuron<{questionType.Name}, {replyType.Name}> but overrides "
            + $"neither {nameof(INeuron<,>.HandleAsync)} nor {nameof(INeuron<,>.Answer)}; "
            + "a never-overridden answerer would defer every ask forever — a dead claim.");
    }
}
