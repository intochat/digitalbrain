using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Corpus;

// Append-only watermarked projection. Not a full journal substitute —
// entries are explicit (schedules, later sources) and resumable by sequence.
[GrainType(ICorpus.GrainTypeName)]
public sealed class CorpusNeuron : Neuron, ICorpus
{
    private const string StateName = "corpus.json";
    private const int RetainMax = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IDurableValue<string> _json;

    public CorpusNeuron()
    {
        _json = ServiceProvider.GetRequiredKeyedService<IDurableValue<string>>(StateName);
    }

    public Task HandleAsync(AppendCorpusEntry synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (string.IsNullOrWhiteSpace(synapse.Kind) || string.IsNullOrWhiteSpace(synapse.Text))
        {
            throw new NeuronAuthorizationException(
                $"Corpus '{Id}' refuses an entry without Kind and Text.");
        }

        var state = Load();
        var sequence = state.Watermark + 1;
        var entry = new CorpusEntry(
            sequence,
            synapse.Kind.Trim(),
            synapse.Text.Trim(),
            string.IsNullOrWhiteSpace(synapse.Correlation) ? null : synapse.Correlation.Trim(),
            synapse.At ?? TimeProvider.GetUtcNow());

        state.Entries.Add(entry);
        while (state.Entries.Count > RetainMax)
        {
            state.Entries.RemoveAt(0);
        }

        state.Watermark = sequence;
        Save(state);
        return ReplyAsync(new CorpusAppended(synapse.CommandId, sequence), cancellationToken);
    }

    public Task HandleAsync(ReadCorpus synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var limit = synapse.Limit <= 0 ? 100 : Math.Min(synapse.Limit, 500);
        var state = Load();
        var page = state.Entries
            .Where(e => e.Sequence > synapse.AfterSequence)
            .OrderBy(e => e.Sequence)
            .Take(limit + 1)
            .ToArray();

        var truncated = page.Length > limit;
        if (truncated)
        {
            page = page.Take(limit).ToArray();
        }

        return ReplyAsync(
            new CorpusPage(synapse.CommandId, state.Watermark, page, truncated),
            cancellationToken);
    }

    public Task HandleAsync(ReadEpisode synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (string.IsNullOrWhiteSpace(synapse.Correlation))
        {
            throw new NeuronAuthorizationException(
                $"Corpus '{Id}' refuses ReadEpisode without a correlation id.");
        }

        var limit = synapse.Limit <= 0 ? 100 : Math.Min(synapse.Limit, 500);
        var correlation = synapse.Correlation.Trim();
        var entries = Load().Entries
            .Where(e => string.Equals(e.Correlation, correlation, StringComparison.Ordinal))
            .OrderBy(e => e.Sequence)
            .Take(limit)
            .ToArray();

        return ReplyAsync(new EpisodePage(synapse.CommandId, correlation, entries), cancellationToken);
    }

    private CorpusState Load()
    {
        var text = _json.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CorpusState();
        }

        return JsonSerializer.Deserialize<CorpusState>(text, JsonOptions) ?? new CorpusState();
    }

    private void Save(CorpusState state)
        => _json.Value = JsonSerializer.Serialize(state, JsonOptions);

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A corpus command requires a command id.");
        }
    }

    private sealed class CorpusState
    {
        public long Watermark { get; set; }

        public List<CorpusEntry> Entries { get; set; } = [];
    }
}
