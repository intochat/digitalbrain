using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;

namespace DigitalBrain.Core.Behaviors;
[GenerateSerializer, Alias("db.v2.processor-state")]
public sealed record BehaviorProcessorState([property: Id(0)] BehaviorNodeActivation Activation, [property: Id(1)] string? Error = null, [property: Id(2)] string? UncertainAction = null, [property: Id(3)] SignalDelivery? PausedInput = null);
/// <summary>Owns validated processing and reuses the snapshot/outbox commit boundary.</summary>
public abstract class BehaviorProcessorNeuron : Neuron<BehaviorProcessorState>, IBehaviorNode
{
    private readonly IDurableValue<string> _attempt;
    protected BehaviorProcessorNeuron(NeuronRuntime runtime, IPersistentState<SnapshotEnvelope<BehaviorProcessorState>> state) : base(runtime, state)
    {
        _attempt = ServiceProvider.GetRequiredKeyedService<IDurableValue<string>>("behavior.action-attempt");
    }

    public async Task Configure(BehaviorNodeActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (!IsOwnedBy(activation.Owner))
        {
            if (!activation.Enabled)
            {
                return; // Rollback may revisit a never-claimed or already-released node.
            }
            throw new InvalidOperationException("Processor configuration requires its durable ownership claim.");
        }
        if (State is { } current)
        {
            if (current.Activation.Owner != activation.Owner)
            {
                throw new InvalidOperationException("Processor belongs to another behavior run.");
            }

            var original = JsonSerializer.SerializeToElement(current.Activation.Definition, BehaviorJson.Default.BehaviorNodeDefinition);
            var requested = JsonSerializer.SerializeToElement(activation.Definition, BehaviorJson.Default.BehaviorNodeDefinition);
            if (!JsonElement.DeepEquals(original, requested))
            {
                throw new InvalidOperationException("A processor definition is immutable within its behavior run.");
            }
            if (current.Activation.Enabled == activation.Enabled)
            {
                return;
            }

            await SaveAsync(current with { Activation = current.Activation with { Enabled = activation.Enabled } }).ConfigureAwait(true);
            return;
        }

        await SaveAsync(new BehaviorProcessorState(activation)).ConfigureAwait(true);
    }

    public Task<BehaviorNodeStatus> Status() => Task.FromResult(new BehaviorNodeStatus(State?.Activation.Enabled ?? false, State?.Error, State?.UncertainAction));
    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (State is not { Activation.Enabled: true } current)
        {
            return;
        }

        if (current.Error is not null)
        {
            throw new InvalidOperationException("Processing is paused; inspect the behavior's diagnostics. Pending inputs remain durable.");
        }

        var node = current.Activation.Definition;
        if (node.Input is null || node.Input.SignalType != delivery.Signal.Type)
        {
            return;
        }

        string? output;
        try
        {
            RequirePayload(node.Input, delivery.Signal.Body);
            using var configuration = JsonDocument.Parse(node.Configuration);
            var config = configuration.RootElement;
            output = node.Capability switch
            {
                "filter" => BehaviorMapping.Matches(config, delivery.Signal.Body) ? delivery.Signal.Body : null,
                "map" => BehaviorMapping.Map(config.GetProperty("template"), delivery.Signal.Body, delivery.SignalId),
                "decision" => await ServiceProvider.GetRequiredService<IBehaviorDecision>().DecideAsync(config.GetProperty("instructions").GetString()!, delivery.Signal.Body, node.Output!.Schema, Optional(config, "provider"), Optional(config, "model"), cancellationToken).ConfigureAwait(true),
                "action" => await InvokeAction(config, delivery, cancellationToken).ConfigureAwait(true),
                _ => throw new InvalidOperationException("Unknown processor capability."),
            };
            if (output is not null && node.Output is { } contract)
            {
                RequirePayload(contract, output);
                Announce(Signal.Create(contract.SignalType, output));
            }
        }
        catch (Exception error) when (error is not OperationCanceledException and not NeuronBusyException and not NeuronRecoveringException and not NeuronPersistenceException)
        {
            // Preserve the failed input and stop later work. No invalid model output or uncertain action is emitted.
            var action = node.Capability == "action" ? BehaviorMapping.ActionId(Id, delivery.SignalId).ToString() : null;
            await SaveAsync(current with { Error = error.Message, UncertainAction = action, PausedInput = delivery }, cancellationToken).ConfigureAwait(true);
            return;
        }

        await SaveAsync(current, cancellationToken).ConfigureAwait(true);
    }

    private async Task<string?> InvokeAction(JsonElement config, SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var target = BehaviorMapping.Target(config);
        var iface = config.GetProperty("interface").GetString()!;
        var method = config.GetProperty("method").GetString()!;
        var invoker = ServiceProvider.GetRequiredService<INeuronInvoker>();
        var descriptor = invoker.Describe(iface, method);
        var commandId = BehaviorMapping.ActionId(Id, delivery.SignalId);
        var identity = invoker.ArgumentContractOf(iface, method)?.CommandIdPropertyName;
        var args = JsonNode.Parse(delivery.Signal.Body);
        long? beforeInvocation = null;
        var firstAttempt = _attempt.Value != commandId.ToString();
        if (!descriptor.IsReadOnly)
        {
            if (identity is null || args is not JsonObject command)
            {
                throw new InvalidOperationException("An action needs a command identity contract.");
            }

            // Caller-supplied ids cannot collapse distinct inputs or change a retried operation's identity.
            command[identity] = JsonSerializer.SerializeToNode(commandId, invoker.ArgumentContractOf(iface, method)!.Options);
            if (_attempt.Value == commandId.ToString())
            {
                var journal = await GrainFactory.GetGrain<INeuron>(target.ToGrainId()).ReadCommands(0).ConfigureAwait(true);
                var previous = journal.Delta.LastOrDefault(record => record.Id == commandId);
                if (previous is null || previous.Phase is CommandPhase.Unknown or CommandPhase.Attempted)
                {
                    throw new InvalidOperationException($"Action {commandId} has an uncertain outcome. Inspect {target}'s command journal before resolving it.");
                }
            }

            _attempt.Value = commandId.ToString();
            await PersistAsync().ConfigureAwait(true);
            beforeInvocation = (await GrainFactory.GetGrain<INeuron>(target.ToGrainId()).ReadCommands(0).ConfigureAwait(true)).ResumeSequence;
        }

        using var document = JsonDocument.Parse(args?.ToJsonString() ?? "null");
        // Repeat validated data with one identity through the existing command deduplication interface.
        try
        {
            var result = await invoker.InvokeAsync(target, iface, method, document.RootElement, cancellationToken).ConfigureAwait(true);
            return result?.GetRawText();
        }
        catch (NeuronBusyException) when (firstAttempt && beforeInvocation is { } sequence)
        {
            // Busy can also arise after the target's callback. Only a complete journal
            // interval without this command proves that admission was rejected safely.
            var interval = await GrainFactory.GetGrain<INeuron>(target.ToGrainId()).ReadCommands(sequence).ConfigureAwait(true);
            if (!interval.Gap && !interval.Delta.Any(record => record.Id == commandId))
            {
                _attempt.Value = string.Empty;
                await PersistAsync().ConfigureAwait(true);
            }
            throw;
        }
    }

    private static string? Optional(JsonElement config, string name) => config.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static void RequirePayload(PayloadContract contract, string body)
    {
        var errors = BehaviorSchema.Validate(contract.Schema, body);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid {contract.SignalType}: {string.Join("; ", errors)}");
        }
    }
}

[GrainType("behavior-filter")]
public sealed class FilterNeuron(NeuronRuntime runtime, [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorProcessorState>> state) : BehaviorProcessorNeuron(runtime, state);
[GrainType("behavior-map")]
public sealed class MappingNeuron(NeuronRuntime runtime, [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorProcessorState>> state) : BehaviorProcessorNeuron(runtime, state);
[GrainType("behavior-action")]
public sealed class ActionNeuron(NeuronRuntime runtime, [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorProcessorState>> state) : BehaviorProcessorNeuron(runtime, state);
[GrainType("behavior-decision")]
public sealed class DecisionNeuron(NeuronRuntime runtime, [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorProcessorState>> state) : BehaviorProcessorNeuron(runtime, state);


