using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Programming;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;

namespace DigitalBrain.Core.Programming;

[GrainType("program")]
internal sealed class ProgramNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ProgramSnapshot>> state)
    : Neuron<ProgramSnapshot>(runtime, state), IProgram
{
    private ProgramSnapshot Current => State ?? new(Id.Name, 0, false, null, [], [], []);
    public Task<ProgramSnapshot> Read() => Task.FromResult(Current);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = Current;
        if (delivery.Signal.Type == "ProgramChange")
        {
            var change = ProgramWire.Read<ProgramMutation>(delivery.Signal.Body);
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
                            throw new ArgumentException("Program id must match its neuron name.");
                        }
                        var validation = ProgramValidator.Validate(definition);
                        if (!validation.Valid)
                        {
                            throw new ArgumentException(string.Join(" ", validation.Errors));
                        }
                        var revision = new ProgramRevision(current.Version + 1, definition, TimeProvider.GetUtcNow());
                        current = current with { Version = revision.Version, Enabled = true, Definition = definition,
                            Versions = [.. current.Versions.TakeLast(49), revision] };
                        Announce(Signal.Create("ProgramRegistered", ProgramWire.Serialize(new { id = Id.Name })), new("program-catalog", "all"));
                        break;
                    case "enabled":
                        if (current.Definition is null)
                        {
                            throw new ArgumentException("Deploy the program before changing its state.");
                        }
                        var stateRevision = new ProgramRevision(current.Version + 1, current.Definition, TimeProvider.GetUtcNow());
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
            current = current with { Receipts = [.. current.Receipts.TakeLast(127), new ProgramReceipt(change.OperationId, error)] };
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
                current = current with { Receipts = [.. current.Receipts.TakeLast(127), new ProgramReceipt(delivery.SignalId.ToString(), error)] };
                Announce(Signal.Create("ProgramRejected", ProgramWire.Serialize(new { programId = Id.Name, signalId = delivery.SignalId.ToString(), error })));
            }
            await SaveAsync(current, cancellationToken).ConfigureAwait(true);
        }
    }

    private ProgramSnapshot Start(ProgramSnapshot current, string runId, JsonElement input)
    {
        if (current.Definition is null)
        {
            throw new ArgumentException("Deploy the program before running it.");
        }
        if (!current.Enabled)
        {
            throw new InvalidOperationException("This program is paused. Enable it before running.");
        }
        ProgramService.RequireId(runId);
        if (current.Runs.Contains(runId, StringComparer.Ordinal))
        {
            return current;
        }
        var start = new ProgramStart(runId, current.Version, current.Definition,
            input.ValueKind == JsonValueKind.Undefined ? ProgramWire.Null : input);
        Announce(Signal.Create("ProgramStart", ProgramWire.Serialize(start)), ProgramService.RunNeuron(Id.Name, runId));
        return current with { Runs = [.. current.Runs.TakeLast(99), runId] };
    }
}

[GrainType("program-catalog")]
internal sealed class ProgramCatalogNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ProgramCatalogState>> state)
    : Neuron<ProgramCatalogState>(runtime, state), IProgramCatalog
{
    public Task<IReadOnlyList<string>> List() => Task.FromResult(State?.Names ?? []);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "ProgramRegistered")
        {
            return;
        }
        using var body = JsonDocument.Parse(delivery.Signal.Body);
        var name = body.RootElement.GetProperty("id").GetString()!;
        if (delivery.Source.Type != "program" || delivery.Source.Name != name)
        {
            return;
        }
        var names = State?.Names ?? [];
        if (!names.Contains(name, StringComparer.Ordinal))
        {
            await SaveAsync(new ProgramCatalogState([.. names, name]), cancellationToken).ConfigureAwait(true);
        }
    }
}
