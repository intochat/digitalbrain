using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;


namespace DigitalBrain.Core.Behavior;

[GrainType("behavior-catalog")]
internal sealed class BehaviorCatalogNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorCatalogState>> state)
    : Neuron<BehaviorCatalogState>(runtime, state), IBehaviorCatalog
{
    public Task<IReadOnlyList<string>> List() => Task.FromResult(State?.Names ?? []);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "BehaviorRegistered")
        {
            return;
        }
        using var body = JsonDocument.Parse(delivery.Signal.Body);
        var name = body.RootElement.GetProperty("id").GetString()!;
        if (delivery.Source.Type != "behavior" || delivery.Source.Name != name)
        {
            return;
        }
        var names = State?.Names ?? [];
        if (!names.Contains(name, StringComparer.Ordinal))
        {
            await SaveAsync(new BehaviorCatalogState([.. names, name]), cancellationToken).ConfigureAwait(true);
        }
    }
}
