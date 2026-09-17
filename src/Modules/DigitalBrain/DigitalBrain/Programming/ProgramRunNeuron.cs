using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Programming;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Core.Programming;

[GrainType("program-run")]
internal sealed class ProgramRunNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ProgramRunState>> state)
    : Neuron<ProgramRunState>(runtime, state), IProgramRun
{
    public Task<ProgramRunSnapshot?> Read() => Task.FromResult(State?.Snapshot);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = State;
        if (delivery.Signal.Type == "ProgramStart")
        {
            if (current is not null)
            {
                return;
            }
            var start = ProgramWire.Read<ProgramStart>(delivery.Signal.Body);
            if (delivery.Source != new NeuronId("program", start.Definition.Id))
            {
                return;
            }
            if (delivery.Source != new NeuronId("program", start.Definition.Id)
                || Id != ProgramService.RunNeuron(start.Definition.Id, start.RunId))
            {
                return;
            }
            current = new(start.Definition, new(start.RunId, start.Definition.Id, start.Version, "Running", start.Input,
                ProgramWire.Null, [], null, TimeProvider.GetUtcNow(), null));
        }
        else if (delivery.Signal.Type == "ProgramCompleted" && current is { Snapshot.Status: "Running" })
        {
            var completion = ProgramWire.Read<ProgramCompletion>(delivery.Signal.Body);
            if (current.Snapshot.Steps.Any(step => step.NodeId == completion.Step.NodeId))
            {
                return;
            }
            var order = ProgramValidator.Validate(current.Definition).Order;
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
        else if (delivery.Signal.Type == "ProgramCancel" && current is { Snapshot.Status: "Running" })
        {
            ServiceProvider.GetService<IProgramRunCancellation>()?.Cancel(current.Snapshot.ProgramId, current.Snapshot.RunId);
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
            var error = $"Program dispatch failed: {failure.Message}";
            current = current with { Snapshot = current.Snapshot with
            {
                Status = "Failed", Output = ProgramWire.Null,
                Error = error.Length > 2_000 ? error[..2_000] : error,
                CompletedAt = TimeProvider.GetUtcNow(),
            } };
            AnnounceFinished(current.Snapshot);
        }
        if (current.Snapshot.Status != "Running" && ServiceProvider.GetService<IProgramRunLifecycle>() is { } lifecycle)
        {
            await lifecycle.FinishedAsync(current.Definition, current.Snapshot, cancellationToken).ConfigureAwait(true);
        }
        await SaveAsync(current, cancellationToken).ConfigureAwait(true);
    }

    private ProgramRunState Advance(ProgramRunState current)
    {
        var snapshot = current.Snapshot;
        var order = ProgramValidator.Validate(current.Definition).Order;
        if (snapshot.Steps.Count == order.Count)
        {
            var sinks = snapshot.Steps.Where(step => !current.Definition.Synapses.Any(edge => edge.From == step.NodeId)
                && step.Status == "Completed").ToArray();
            var output = sinks.Length == 1 ? sinks[0].Output
                : JsonSerializer.SerializeToElement(sinks.ToDictionary(step => step.NodeId, step => step.Output), ProgramWire.Json);
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
            _ => JsonSerializer.SerializeToElement(values.ToDictionary(step => step.NodeId, step => step.Output), ProgramWire.Json),
        };
        // Prior inputs are already represented by the root input and previous outputs.
        // Keep them out of each subsequent delivery to avoid quadratic payload growth.
        var previous = snapshot.Steps.Select(step => step with { Input = ProgramWire.Null, Error = null }).ToArray();
        var work = new ProgramWork(snapshot.ProgramId, snapshot.RunId, snapshot.Version, node, snapshot.Input, value, previous, skip);
        Announce(Signal.Create("ProgramStep", ProgramWire.Serialize(work)), StepNeuron(snapshot, node.Id));
        return current;
    }

    private void AnnounceFinished(ProgramRunSnapshot snapshot)
        => Announce(Signal.Create("ProgramFinished", ProgramWire.Serialize(new
        {
            snapshot.RunId, snapshot.ProgramId, snapshot.Version, snapshot.Status, snapshot.Output,
            snapshot.Error, snapshot.StartedAt, snapshot.CompletedAt,
        })));

    private static NeuronId StepNeuron(ProgramRunSnapshot run, string nodeId)
    {
        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nodeId)))[..24];
        return new("program-step", $"{run.ProgramId}/{run.RunId}/{suffix}");
    }
}

[GrainType("program-step")]
internal sealed class ProgramStepNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ProgramStep>> state)
    : Neuron<ProgramStep>(runtime, state)
{
    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "ProgramStep" || State is not null)
        {
            return;
        }
        var work = ProgramWire.Read<ProgramWork>(delivery.Signal.Body);
        if (delivery.Source != ProgramService.RunNeuron(work.ProgramId, work.RunId))
        {
            return;
        }
        var run = await GrainFactory.GetGrain<IProgramRun>(delivery.Source.Name).Read().ConfigureAwait(true);
        if (run is not { Status: "Running" })
        {
            await SaveAsync(new ProgramStep(work.Node.Id, work.Node.Kind, "Skipped", ProgramWire.Null,
                ProgramWire.Null, "The run is no longer active.", TimeProvider.GetUtcNow(), TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(true);
            return;
        }
        var start = TimeProvider.GetUtcNow();
        var output = ProgramWire.Null;
        string? error = null;
        var status = work.Skip ? "Skipped" : "Completed";
        try
        {
            if (!work.Skip)
            {
                var outputs = work.Previous.ToDictionary(step => step.NodeId, step => step.Output, StringComparer.Ordinal);
                if (work.Node.Kind.ToLowerInvariant() is "call" or "agent" or "code")
                {
                    var executor = ServiceProvider.GetRequiredService<IProgramNodeExecutor>();
                    output = await executor.ExecuteAsync(new(work.ProgramId, work.RunId, work.Version,
                        work.Node with { Kind = work.Node.Kind.ToLowerInvariant() }, work.Input, work.Value, outputs), cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    var result = ProgramBuiltins.Execute(work.Node, work.Input, work.Value, outputs);
                    output = result.Output;
                    if (result.Stop)
                    {
                        status = "Filtered";
                    }
                }
                output = ProgramExpressions.Clone(output);
                // Reject before committing a result which cannot travel in the durable outbox.
                _ = Signal.Create("ProgramValue", output.GetRawText());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            status = "Failed";
            error = failure.Message.Length > 2000 ? failure.Message[..2000] : failure.Message;
        }
        var step = new ProgramStep(work.Node.Id, work.Node.Kind, status, work.Value, output, error, start, TimeProvider.GetUtcNow());
        Signal completion;
        try
        {
            completion = Signal.Create("ProgramCompleted", ProgramWire.Serialize(new ProgramCompletion(step)));
        }
        catch (SignalRejectedException failure)
        {
            step = step with { Status = "Failed", Input = ProgramWire.Null, Output = ProgramWire.Null,
                Error = $"Step result exceeds the durable delivery limit. Return smaller data or an external reference. {failure.Message}" };
            completion = Signal.Create("ProgramCompleted", ProgramWire.Serialize(new ProgramCompletion(step)));
        }
        Announce(completion, delivery.Source);
        await SaveAsync(step, cancellationToken).ConfigureAwait(true);
    }
}
