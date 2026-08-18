using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Memory;

// Append-only watermarked projection of durable story facts. Not a full journal substitute —
// facts are explicit (turn completions, schedule ticks, later sources) and text-first;
// embedding is a separate concern (VectorMemoryNeuron) left to whichever caller wants a fact
// searchable too.
[GrainType(IFactMemory.GrainTypeName)]
public sealed class FactMemoryNeuron : Neuron, IFactMemory
{
    private const string StateName = "memory.facts.state";
    private const int RetainMax = 4096;

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<FactMemoryState> _states;

    public FactMemoryNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<FactMemoryState>>();
    }

    public Task HandleAsync(StoreFact synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (string.IsNullOrWhiteSpace(synapse.Kind) || string.IsNullOrWhiteSpace(synapse.Text))
        {
            throw new NeuronAuthorizationException(
                $"Memory '{Id}' refuses a fact without Kind and Text.");
        }

        var state = Load();
        var sequence = state.Watermark + 1;
        var fact = new FactEntry(
            sequence,
            synapse.Kind.Trim(),
            synapse.Text.Trim(),
            string.IsNullOrWhiteSpace(synapse.Correlation) ? null : synapse.Correlation.Trim(),
            synapse.At ?? TimeProvider.GetUtcNow());

        state.Facts.Add(fact);
        while (state.Facts.Count > RetainMax)
        {
            state.Facts.RemoveAt(0);
        }

        state.Watermark = sequence;
        Save(state);
        return ReplyAsync(new FactStored(synapse.CommandId, sequence), cancellationToken);
    }

    public Task HandleAsync(ReadFacts synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var limit = synapse.Limit <= 0 ? 100 : Math.Min(synapse.Limit, 500);
        var kind = string.IsNullOrWhiteSpace(synapse.Kind) ? null : synapse.Kind.Trim();
        var correlation = string.IsNullOrWhiteSpace(synapse.Correlation) ? null : synapse.Correlation.Trim();

        var state = Load();
        var matches = state.Facts
            .Where(f => kind is null || string.Equals(f.Kind, kind, StringComparison.Ordinal))
            .Where(f => correlation is null || string.Equals(f.Correlation, correlation, StringComparison.Ordinal))
            .OrderBy(f => f.Sequence)
            .ToArray();

        var truncated = matches.Length > limit;
        var page = truncated ? matches.Take(limit).ToArray() : matches;

        return ReplyAsync(
            new FactsRead(synapse.CommandId, state.Watermark, page, truncated),
            cancellationToken);
    }

    private FactMemoryState Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : new FactMemoryState();

    private void Save(FactMemoryState state)
        => _state.Value = _states.SerializeToArray(state);

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A memory-fact command requires a command id.");
        }
    }
}
