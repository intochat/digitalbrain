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

[GrainType("behavior-step")]
internal sealed class BehaviorStepNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorStep>> state)
    : Neuron<BehaviorStep>(runtime, state)
{
    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "BehaviorStep" || State is not null)
        {
            return;
        }
        var work = BehaviorWire.Read<BehaviorWork>(delivery.Signal.Body);
        if (delivery.Source != BehaviorService.RunNeuron(work.BehaviorId, work.RunId))
        {
            return;
        }
        var run = await GrainFactory.GetGrain<IBehaviorRun>(delivery.Source.Name).Read().ConfigureAwait(true);
        if (run is not { Status: "Running" })
        {
            await SaveAsync(new BehaviorStep(work.Node.Id, work.Node.Kind, "Skipped", BehaviorWire.Null,
                BehaviorWire.Null, "The run is no longer active.", TimeProvider.GetUtcNow(), TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(true);
            return;
        }
        var start = TimeProvider.GetUtcNow();
        var output = BehaviorWire.Null;
        string? error = null;
        var status = work.Skip ? "Skipped" : "Completed";
        try
        {
            if (!work.Skip)
            {
                var outputs = work.Previous.ToDictionary(step => step.NodeId, step => step.Output, StringComparer.Ordinal);
                if (work.Node.Kind.ToLowerInvariant() is "call" or "agent" or "code")
                {
                    var executor = ServiceProvider.GetRequiredService<IBehaviorNodeExecutor>();
                    output = await executor.ExecuteAsync(new(work.BehaviorId, work.RunId, work.Version,
                        work.Node with { Kind = work.Node.Kind.ToLowerInvariant() }, work.Input, work.Value, outputs), cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    var result = BehaviorBuiltins.Execute(work.Node, work.Input, work.Value, outputs);
                    output = result.Output;
                    if (result.Stop)
                    {
                        status = "Filtered";
                    }
                }
                output = BehaviorExpressions.Clone(output);
                // Reject before committing a result which cannot travel in the durable outbox.
                _ = Signal.Create("BehaviorValue", output.GetRawText());
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
        var step = new BehaviorStep(work.Node.Id, work.Node.Kind, status, work.Value, output, error, start, TimeProvider.GetUtcNow());
        Signal completion;
        try
        {
            completion = Signal.Create("BehaviorCompleted", BehaviorWire.Serialize(new BehaviorCompletion(step)));
        }
        catch (SignalRejectedException failure)
        {
            step = step with { Status = "Failed", Input = BehaviorWire.Null, Output = BehaviorWire.Null,
                Error = $"Step result exceeds the durable delivery limit. Return smaller data or an external reference. {failure.Message}" };
            completion = Signal.Create("BehaviorCompleted", BehaviorWire.Serialize(new BehaviorCompletion(step)));
        }
        Announce(completion, delivery.Source);
        await SaveAsync(step, cancellationToken).ConfigureAwait(true);
    }
}
