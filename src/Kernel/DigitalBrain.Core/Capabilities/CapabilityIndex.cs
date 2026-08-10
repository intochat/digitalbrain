using System.Text;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Core;

public sealed record CapabilityHit(
    string Kind,
    string ContractId,
    string Signature,
    string? NeuronContractId,
    string? DefaultInstanceName,
    double Score)
{
    public const string RequestKind = "request";
    public const string FactKind = "fact";
}

// The system's own vocabulary, rebuilt from contracts every boot: tiny, in-process,
// and incapable of stalling a turn. Owner memories live elsewhere (Qdrant).
public sealed class CapabilityIndex
{
    private const double VectorWeight = 0.6;
    private const double KeywordWeight = 0.4;
    private static readonly TimeSpan EmbeddingBudget = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<Entry> _entries;
    private readonly object _enrichment = new();
    private Task? _enriching;

    private CapabilityIndex(IReadOnlyList<Entry> entries) => _entries = entries;

    public static CapabilityIndex Build(IReadOnlyList<CapabilityManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        var entries = new List<Entry>();
        var indexed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var manifest in manifests)
        {
            foreach (var neuron in manifest.Neurons)
            {
                foreach (var accepted in neuron.Accepted)
                {
                    if (indexed.Add(accepted.ContractId))
                    {
                        entries.Add(Entry.For(
                            CapabilityHit.RequestKind,
                            accepted.ContractId,
                            neuron.ContractId,
                            neuron.DefaultInstanceName));
                    }
                }
            }

            foreach (var fact in manifest.Facts)
            {
                if (indexed.Add(fact.ContractId))
                {
                    entries.Add(Entry.For(CapabilityHit.FactKind, fact.ContractId, null, null));
                }
            }
        }

        return new CapabilityIndex(entries);
    }

    public IReadOnlyList<CapabilityHit> Find(string intent, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var query = Tokens(intent);
        if (query.Count == 0)
        {
            return [];
        }

        return
        [
            .. _entries
                .Select(entry => entry.Hit(KeywordScore(query, entry)))
                .Where(static hit => hit.Score > 0)
                .OrderByDescending(static hit => hit.Score)
                .ThenBy(static hit => hit.ContractId, StringComparer.Ordinal)
                .Take(limit),
        ];
    }

    public async Task<IReadOnlyList<CapabilityHit>> FindAsync(
        string intent,
        int limit,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings,
        CancellationToken cancellationToken)
    {
        var keywordRanked = Find(intent, limit);
        if (embeddings is null)
        {
            return keywordRanked;
        }

        BeginEnrichment(embeddings);
        if (_entries.All(static entry => entry.Vector is null))
        {
            return keywordRanked;
        }

        float[] query;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(EmbeddingBudget);
            var embedded = await embeddings
                .GenerateAsync([intent], cancellationToken: budget.Token)
                .ConfigureAwait(false);
            query = embedded[0].Vector.ToArray();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return keywordRanked;
        }

        var tokens = Tokens(intent);
        return
        [
            .. _entries
                .Select(entry =>
                {
                    var keyword = KeywordScore(tokens, entry);
                    var vector = entry.Vector is { } stored
                        ? Math.Max(0, Cosine(stored, query))
                        : 0;
                    return entry.Hit(VectorWeight * vector + KeywordWeight * keyword);
                })
                .Where(static hit => hit.Score > 0.2)
                .OrderByDescending(static hit => hit.Score)
                .ThenBy(static hit => hit.ContractId, StringComparer.Ordinal)
                .Take(limit),
        ];
    }

    private void BeginEnrichment(IEmbeddingGenerator<string, Embedding<float>> embeddings)
    {
        if (_enriching is not null)
        {
            return;
        }

        lock (_enrichment)
        {
            _enriching ??= Task.Run(async () =>
            {
                using var budget = new CancellationTokenSource(EmbeddingBudget);
                var embedded = await embeddings
                    .GenerateAsync(_entries.Select(static entry => entry.Text), cancellationToken: budget.Token)
                    .ConfigureAwait(false);

                for (var index = 0; index < _entries.Count && index < embedded.Count; index++)
                {
                    _entries[index].Vector = embedded[index].Vector.ToArray();
                }
            });
        }
    }

    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return 0;
        }

        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += (double)left[index] * right[index];
            leftLength += (double)left[index] * left[index];
            rightLength += (double)right[index] * right[index];
        }

        return leftLength == 0 || rightLength == 0
            ? 0
            : dot / (Math.Sqrt(leftLength) * Math.Sqrt(rightLength));
    }

    private static double KeywordScore(IReadOnlyCollection<string> query, Entry entry)
    {
        var matched = query.Count(entry.Haystack.Contains);
        return matched == 0 ? 0 : (double)matched / query.Count;
    }

    internal static IReadOnlyCollection<string> Tokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var word = new StringBuilder();

        void Cut()
        {
            if (word.Length > 2)
            {
                tokens.Add(word.ToString());
            }

            word.Clear();
        }

        foreach (var letter in text)
        {
            if (char.IsLetterOrDigit(letter))
            {
                if (char.IsUpper(letter) && word.Length > 0 && !char.IsUpper(word[^1]))
                {
                    Cut();
                }

                word.Append(char.ToLowerInvariant(letter));
                continue;
            }

            Cut();
        }

        Cut();
        return tokens;
    }

    private sealed class Entry
    {
        private Entry(string kind, string contractId, string? neuron, string? instance, string signature, IReadOnlyCollection<string> haystack, string text)
        {
            Kind = kind;
            ContractId = contractId;
            Neuron = neuron;
            Instance = instance;
            Signature = signature;
            Haystack = haystack;
            Text = text;
        }

        internal string Kind { get; }
        internal string ContractId { get; }
        internal string? Neuron { get; }
        internal string? Instance { get; }
        internal string Signature { get; }
        internal IReadOnlyCollection<string> Haystack { get; }
        internal string Text { get; }
        internal float[]? Vector { get; set; }

        internal CapabilityHit Hit(double score)
            => new(Kind, ContractId, Signature, Neuron, Instance, score);

        internal static Entry For(string kind, string contractId, string? neuron, string? instance)
        {
            var synapseType = SynapseTypeIndex.FindByAlias(contractId);
            var signature = synapseType is null ? contractId : ContractSignature.Of(synapseType);
            var searchable = string.Join(
                ' ',
                new[] { contractId, neuron, signature }.Where(static part => !string.IsNullOrWhiteSpace(part)));

            return new Entry(kind, contractId, neuron, instance, signature, Tokens(searchable), searchable);
        }
    }
}

public static class ContractSignature
{
    private const string AutoFilledCommandId = "CommandId";

    public static string Of(Type synapseType)
    {
        ArgumentNullException.ThrowIfNull(synapseType);

        var parameters = synapseType
            .GetConstructors()
            .OrderByDescending(static ctor => ctor.GetParameters().Length)
            .FirstOrDefault()?
            .GetParameters()
            .Where(static parameter => parameter.Name != null
                && !string.Equals(parameter.Name, AutoFilledCommandId, StringComparison.OrdinalIgnoreCase))
            .Select(static parameter =>
                $"{char.ToLowerInvariant(parameter.Name![0])}{parameter.Name[1..]}: {Named(parameter.ParameterType)}")
            ?? [];

        var reply = ReplyOf(synapseType);
        var rendered = $"{synapseType.Name}({string.Join(", ", parameters)})";
        return reply is null ? rendered : $"{rendered} → {reply.Name}";
    }

    private static Type? ReplyOf(Type synapseType)
    {
        for (var probed = synapseType.BaseType; probed is not null; probed = probed.BaseType)
        {
            if (probed.IsGenericType && probed.GetGenericTypeDefinition() == typeof(RequestSynapse<>))
            {
                return probed.GenericTypeArguments[0];
            }
        }

        return null;
    }

    private static string Named(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return $"{Named(inner)}?";
        }

        return type switch
        {
            _ when type == typeof(int) => "int",
            _ when type == typeof(long) => "long",
            _ when type == typeof(bool) => "bool",
            _ when type == typeof(double) => "double",
            _ when type == typeof(string) => "string",
            _ when type.IsArray => $"{Named(type.GetElementType()!)}[]",
            _ => type.Name,
        };
    }
}
