using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;


namespace DigitalBrain.Core.Behavior;

[GrainType("behavior-run")]
internal sealed class BehaviorRunNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorRunState>> state)
    : Neuron<BehaviorRunState>(runtime, state), IBehaviorRun
{
    public Task<BehaviorRunSnapshot?> Read() => Task.FromResult(State?.Snapshot);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = State;
        if (delivery.Signal.Type == "BehaviorStart")
        {
            if (current is not null)
            {
                return;
            }
            var start = BehaviorWire.Read<BehaviorStart>(delivery.Signal.Body);
            if (delivery.Source != new NeuronId("behavior", start.Definition.Id))
            {
                return;
            }
            if (delivery.Source != new NeuronId("behavior", start.Definition.Id)
                || Id != BehaviorService.RunNeuron(start.Definition.Id, start.RunId))
            {
                return;
            }
            current = new(start.Definition, new(start.RunId, start.Definition.Id, start.Version, "Running", start.Input,
                BehaviorWire.Null, [], null, TimeProvider.GetUtcNow(), null));
        }
        else if (delivery.Signal.Type == "BehaviorCompleted" && current is { Snapshot.Status: "Running" })
        {
            var completion = BehaviorWire.Read<BehaviorCompletion>(delivery.Signal.Body);
            if (current.Snapshot.Steps.Any(step => step.NodeId == completion.Step.NodeId))
            {
                return;
            }
            var order = BehaviorValidator.Validate(current.Definition).Order;
            var expected = order[current.Snapshot.Steps.Count];
            var expectedSource = StepNeuron(current.Snapshot, expected);
            if (delivery.Source != expectedSource || completion.Step.NodeId != expected)
            {
                return;
            }
            current = current with { Snapshot = current.Snapshot with { Steps = [.. current.Snapshot.Steps, completion.Step] } };
            if (completion.Step.Status == "Failed")
            {
                current = current with { Snapshot = current.Snapshot with { Status = "Failed", Error = completion.Step.Error, CompletedAt = TimeProvider.GetUtcNow() } };
            }
        }
        else if (delivery.Signal.Type == "BehaviorCancel" && current is { Snapshot.Status: "Running" })
        {
            ServiceProvider.GetService<IBehaviorRunCancellation>()?.Cancel(current.Snapshot.BehaviorId, current.Snapshot.RunId);
            current = current with { Snapshot = current.Snapshot with { Status = "Cancelled", CompletedAt = TimeProvider.GetUtcNow() } };
        }
        else
        {
            return;
        }

        try
        {
            if (current.Snapshot.Status == "Running")
            {
                current = Advance(current);
            }
            if (current.Snapshot.Status != "Running")
            {
                AnnounceFinished(current.Snapshot);
            }
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or OverflowException or JsonException)
        {
            var error = $"Behavior dispatch failed: {failure.Message}";
            current = current with { Snapshot = current.Snapshot with
            {
                Status = "Failed", Output = BehaviorWire.Null,
                Error = error.Length > 2_000 ? error[..2_000] : error,
                CompletedAt = TimeProvider.GetUtcNow(),
            } };
            AnnounceFinished(current.Snapshot);
        }
        if (current.Snapshot.Status != "Running" && ServiceProvider.GetService<IBehaviorRunLifecycle>() is { } lifecycle)
        {
            await lifecycle.FinishedAsync(current.Definition, current.Snapshot, cancellationToken).ConfigureAwait(true);
        }
        await SaveAsync(current, cancellationToken).ConfigureAwait(true);
    }

    private BehaviorRunState Advance(BehaviorRunState current)
    {
        var snapshot = current.Snapshot;
        var order = BehaviorValidator.Validate(current.Definition).Order;
        if (snapshot.Steps.Count == order.Count)
        {
            var sinks = snapshot.Steps.Where(step => !current.Definition.Synapses.Any(edge => edge.From == step.NodeId)
                && step.Status == "Completed").ToArray();
            var output = sinks.Length == 1 ? sinks[0].Output
                : JsonSerializer.SerializeToElement(sinks.ToDictionary(step => step.NodeId, step => step.Output), BehaviorWire.Json);
            var finished = snapshot with { Status = sinks.Length == 0 ? "Filtered" : "Completed", Output = output, CompletedAt = TimeProvider.GetUtcNow() };
            return current with { Snapshot = finished };
        }
        var node = current.Definition.Nodes.Single(item => item.Id == order[snapshot.Steps.Count]);
        var predecessors = current.Definition.Synapses.Where(edge => edge.To == node.Id)
            .Select(edge => snapshot.Steps.Single(step => step.NodeId == edge.From)).ToArray();
        var skip = predecessors.Length > 0 && predecessors.All(step => step.Status is "Filtered" or "Skipped");
        var values = predecessors.Where(step => step.Status == "Completed").ToArray();
        var value = values.Length switch
        {
            0 => snapshot.Input,
            1 => values[0].Output,
            _ => JsonSerializer.SerializeToElement(values.ToDictionary(step => step.NodeId, step => step.Output), BehaviorWire.Json),
        };
        // Prior inputs are already represented by the root input and previous outputs.
        // Keep them out of each subsequent delivery to avoid quadratic payload growth.
        var previous = snapshot.Steps.Select(step => step with { Input = BehaviorWire.Null, Error = null }).ToArray();
        var work = new BehaviorWork(snapshot.BehaviorId, snapshot.RunId, snapshot.Version, node, snapshot.Input, value, previous, skip);
        Announce(Signal.Create("BehaviorStep", BehaviorWire.Serialize(work)), StepNeuron(snapshot, node.Id));
        return current;
    }

    private void AnnounceFinished(BehaviorRunSnapshot snapshot)
        => Announce(Signal.Create("BehaviorFinished", BehaviorWire.Serialize(new
        {
            snapshot.RunId, snapshot.BehaviorId, snapshot.Version, snapshot.Status, snapshot.Output,
            snapshot.Error, snapshot.StartedAt, snapshot.CompletedAt,
        })));

    private static NeuronId StepNeuron(BehaviorRunSnapshot run, string nodeId)
    {
        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nodeId)))[..24];
        return new("behavior-step", $"{run.BehaviorId}/{run.RunId}/{suffix}");
    }
}
