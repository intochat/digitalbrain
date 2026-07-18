using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Ino.Experiences;

// MemoryNeuron: reacts to Remember/Recall synapses.
// Durable journal via base Neuron (Orleans.Journaling IDurableList Incoming/Outgoing).
// Memory mirror rebuilt on activate by scanning the restored durable list for past RememberSynapse.
[GrainType("memory")]
public sealed class MemoryNeuron()
    : Neuron(),
      IHandle<RememberSynapse>,
      IHandle<RecallQuerySynapse>
{
    private readonly Dictionary<string, string> _memory = new(StringComparer.OrdinalIgnoreCase);

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Rebuild from the durable journal list (restored by Journaling on activation).
        _memory.Clear();
        foreach (var e in Incoming.OfType<RememberSynapse>())
        {
            var k = string.IsNullOrWhiteSpace(e.CorrelationScope) ? e.Key : $"{e.CorrelationScope}:{e.Key}";
            _memory[k] = e.Value;
        }
    }

    public async Task HandleAsync(RememberSynapse mem, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(mem.CorrelationScope) ? mem.Key : $"{mem.CorrelationScope}:{mem.Key}";
        _memory[key] = mem.Value;

        await Emit(new NeuronTelemetry(Self, "MemoryRemembered", new Dictionary<string, string>
        {
            ["key"] = mem.Key,
            ["scope"] = mem.CorrelationScope ?? ""
        }));
    }

    public async Task HandleAsync(RecallQuerySynapse query, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(query.CorrelationScope) ? query.Key : $"{query.CorrelationScope}:{query.Key}";
        var value = _memory.TryGetValue(key, out var v) ? v : null;

        var result = value is not null
            ? new RecallResult(new RecallHit(query.Key, value))
            : new RecallResult(new RecallMiss(query.Key));

        var recallSyn = new MemoryRecallSynapse(query, result);
        await Emit(recallSyn);
        // Memory surface removed (direct Card); rule in os/memory.ino on: MemoryRecall produces "Memory $key" card.
        // (Note: actual emitted is MemoryRecallSynapse; rule on: may use telemetry or adjust for exact match in future.)

    }
}
