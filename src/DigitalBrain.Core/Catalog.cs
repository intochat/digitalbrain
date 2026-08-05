using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain;

internal sealed class Catalog
{
    private static readonly Type[] ReservedKinds =
        [typeof(Connect), typeof(Disconnect), typeof(Schedule), typeof(Unschedule)];

    private static readonly Type[] CoreVocabulary =
    [
        typeof(Connect), typeof(Disconnect), typeof(ConnectionRefused), typeof(DeliveryFailed),
        typeof(AskExpired), typeof(Schedule), typeof(Unschedule), typeof(ScheduleFailed),
    ];

    private readonly Dictionary<string, Type> kinds;
    private readonly Dictionary<string, Type> factKinds;
    private readonly Dictionary<Type, string> factKindNames;
    private readonly Dictionary<Type, HashSet<string>> listeners;
    private readonly Dictionary<Type, string> answerers;
    private readonly Dictionary<Type, Type> replyTypes;

    private Catalog(
        Dictionary<string, Type> kinds,
        Dictionary<string, Type> factKinds,
        Dictionary<Type, string> factKindNames,
        Dictionary<Type, HashSet<string>> listeners,
        Dictionary<Type, string> answerers,
        Dictionary<Type, Type> replyTypes,
        string fingerprint)
    {
        this.kinds = kinds;
        this.factKinds = factKinds;
        this.factKindNames = factKindNames;
        this.listeners = listeners;
        this.answerers = answerers;
        this.replyTypes = replyTypes;
        Fingerprint = fingerprint;
    }

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
        var replyTypes = new Dictionary<Type, Type>();
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
                    continue;
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
                else if (shape == typeof(IAnswers<,>))
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
                    replyTypes.Add(questionType, replyType);
                    answeredQuestions.Add(questionType);
                    rows.Add($"{kind} answers {NeuronId.KindOf(questionType)}→{NeuronId.KindOf(replyType)}");
                }
            }

            foreach (var questionType in answeredQuestions)
            {
                if (heardFacts.Contains(questionType))
                {
                    throw new InvalidOperationException(
                        $"{Describe(neuronType)} declares both INeuron<{questionType.Name}> and "
                        + $"IAnswers<{questionType.Name}, {replyTypes[questionType].Name}> for one question; "
                        + "a kind is either a listener or the answerer, never both.");
                }
            }
        }

        var payload = string.Join('\n', rows.Order(StringComparer.Ordinal));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        return new Catalog(kinds, factKinds, factKindNames, listeners, answerers, replyTypes, fingerprint);
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

    internal bool IsQuestion(Type factType) => answerers.ContainsKey(factType);

    internal bool TryGetReplyType(Type questionType, [NotNullWhen(true)] out Type? replyType)
        => replyTypes.TryGetValue(questionType, out replyType);

    internal Type ReplyTypeOf(Type questionType)
        => TryGetReplyType(questionType, out var replyType)
            ? replyType
            : throw new InvalidOperationException(
                $"{Describe(questionType)} has no answerer in the composition; only IAnswers questions carry a reply type.");

    // Asker may continue when it declares INeuron for the question's reply type.
    internal bool HasContinuation(string neuronKind, Type questionType)
        => TryGetReplyType(questionType, out var replyType)
        && ListenerKindsOf(replyType).Contains(neuronKind);

    internal bool ListensTo(string neuronKind, Type factType)
        => ListenerKindsOf(factType).Contains(neuronKind);

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
                $"{Describe(neuronType)} declares an interface over abstract synapse {Describe(factType)}; "
                + "dispatch is exact-declared-type — declare sealed concrete facts.");
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
}
