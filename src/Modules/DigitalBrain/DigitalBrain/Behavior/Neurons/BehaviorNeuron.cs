using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;


namespace DigitalBrain.Core.Behavior;

[GrainType("behavior")]
internal sealed class BehaviorNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorSnapshot>> state)
    : Neuron<BehaviorSnapshot>(runtime, state), IBehavior
{
    private BehaviorSnapshot Current => State ?? new(Id.Name, 0, false, null, [], [], []);
    public Task<BehaviorSnapshot> Read() => Task.FromResult(Current);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = Current;
        if (delivery.Signal.Type == "BehaviorChange")
        {
            var change = BehaviorWire.Read<BehaviorMutation>(delivery.Signal.Body);
            if (current.Receipts.Any(receipt => receipt.OperationId == change.OperationId))
            {
                return;
            }
            string? error = null;
            try
            {
                if (change.ExpectedVersion is { } expected && expected != current.Version)
                {
                    throw new InvalidOperationException($"Version conflict: expected {expected}, current {current.Version}. Read the program and retry.");
                }
                switch (change.Action)
                {
                    case "deploy":
                    case "rollback":
                        var definition = change.Action == "rollback"
                            ? current.Versions.FirstOrDefault(version => version.Version == change.Version)?.Definition
                            : change.Definition;
                        if (definition is null)
                        {
                            throw new ArgumentException("The requested program definition or revision does not exist.");
                        }
                        if (definition.Id != Id.Name)
                        {
                            throw new ArgumentException("Behavior id must match its neuron name.");
                        }
                        var validation = BehaviorValidator.Validate(definition);
                        if (!validation.Valid)
                        {
                            throw new ArgumentException(string.Join(" ", validation.Errors));
                        }
                        var revision = new BehaviorRevision(current.Version + 1, definition, TimeProvider.GetUtcNow());
                        current = current with { Version = revision.Version, Enabled = true, Definition = definition,
                            Versions = [.. current.Versions.TakeLast(49), revision] };
                        Announce(Signal.Create("BehaviorRegistered", BehaviorWire.Serialize(new { id = Id.Name })), new("behavior-catalog", "all"));
                        break;
                    case "enabled":
                        if (current.Definition is null)
                        {
                            throw new ArgumentException("Deploy the program before changing its state.");
                        }
                        var stateRevision = new BehaviorRevision(current.Version + 1, current.Definition, TimeProvider.GetUtcNow());
                        current = current with { Enabled = change.Enabled ?? false, Version = stateRevision.Version,
                            Versions = [.. current.Versions.TakeLast(49), stateRevision] };
                        break;
                    case "run":
                        current = Start(current, change.RunId ?? change.OperationId, change.Input);
                        break;
                    default:
                        throw new ArgumentException("Unknown program operation.");
                }
            }
            catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
            {
                error = failure.Message;
            }
            current = current with { Receipts = [.. current.Receipts.TakeLast(127), new BehaviorReceipt(change.OperationId, error)] };
            await SaveAsync(current, cancellationToken).ConfigureAwait(true);
            return;
        }

        if (current.Enabled && current.Definition?.Trigger == delivery.Signal.Type)
        {
            try
            {
                using var body = JsonDocument.Parse(delivery.Signal.Body);
                current = Start(current, delivery.SignalId.ToString(), body.RootElement.Clone());
            }
            catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
            {
                var error = failure.Message.Length > 2_000 ? failure.Message[..2_000] : failure.Message;
                current = current with { Receipts = [.. current.Receipts.TakeLast(127), new BehaviorReceipt(delivery.SignalId.ToString(), error)] };
                Announce(Signal.Create("BehaviorRejected", BehaviorWire.Serialize(new { behaviorId = Id.Name, signalId = delivery.SignalId.ToString(), error })));
            }
            await SaveAsync(current, cancellationToken).ConfigureAwait(true);
        }
    }

    private BehaviorSnapshot Start(BehaviorSnapshot current, string runId, JsonElement input)
    {
        if (current.Definition is null)
        {
            throw new ArgumentException("Deploy the program before running it.");
        }
        if (!current.Enabled)
        {
            throw new InvalidOperationException("This program is paused. Enable it before running.");
        }
        BehaviorService.RequireId(runId);
        if (current.Runs.Contains(runId, StringComparer.Ordinal))
        {
            return current;
        }
        var start = new BehaviorStart(runId, current.Version, current.Definition,
            input.ValueKind == JsonValueKind.Undefined ? BehaviorWire.Null : input);
        Announce(Signal.Create("BehaviorStart", BehaviorWire.Serialize(start)), BehaviorService.RunNeuron(Id.Name, runId));
        return current with { Runs = [.. current.Runs.TakeLast(99), runId] };
    }
}
