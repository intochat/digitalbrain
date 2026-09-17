using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Abstractions.Signals;


namespace DigitalBrain.Core.Behavior;

public sealed partial class BehaviorService(IGrainFactory grains, INeuronInvoker invoker)
{
    public BehaviorValidation Validate(BehaviorDefinition definition)
    {
        var validation = BehaviorValidator.Validate(definition);
        if (!validation.Valid)
        {
            return validation;
        }
        var errors = new List<string>();
        foreach (var node in definition.Nodes.Where(node => node.Kind.Equals("call", StringComparison.OrdinalIgnoreCase)))
        {
            var address = node.Config.GetProperty("neuron").GetString();
            var contract = node.Config.GetProperty("interface").GetString()!;
            var method = node.Config.GetProperty("method").GetString()!;
            if (!NeuronId.TryParse(address, out var neuron))
            {
                errors.Add($"Node '{node.Id}' needs a concrete neuron address.");
                continue;
            }
            if (!invoker.Describe(neuron).Any(capability => capability.InterfaceAlias == contract && capability.MethodAlias == method))
            {
                errors.Add($"Node '{node.Id}': {neuron} does not expose {contract}/{method}. Use MCP describe to discover its capabilities.");
            }
        }
        return new(errors.Count == 0, errors, validation.Order);
    }

    public static void RequireId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !ValidId().IsMatch(id))
        {
            throw new ArgumentException("Use an id of 1–48 letters, digits, hyphens or underscores, starting with a letter or digit.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,47}$")]
    private static partial Regex ValidId();

    internal static NeuronId RunNeuron(string behaviorId, string runId) => new("behavior-run", $"{behaviorId}/{runId}");

    public async Task<IReadOnlyList<BehaviorSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = await grains.GetGrain<IBehaviorCatalog>("all").List().ConfigureAwait(false);
        return await Task.WhenAll(ids.Select(id => ReadAsync(id, cancellationToken))).ConfigureAwait(false);
    }

    public Task<BehaviorSnapshot> ReadAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireId(id);
        return grains.GetGrain<IBehavior>(id).Read();
    }

    public Task<BehaviorSnapshot> DeployAsync(BehaviorDefinition definition, long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        RequireId(definition.Id);
        var validation = Validate(definition);
        if (!validation.Valid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors));
        }
        return ChangeAsync(definition.Id, new(Guid.NewGuid().ToString("N"), "deploy", definition, expectedVersion, Input: BehaviorWire.Null), cancellationToken);
    }

    public Task<BehaviorSnapshot> SetEnabledAsync(string id, bool enabled, long? expectedVersion = null, CancellationToken cancellationToken = default)
        => ChangeAsync(id, new(Guid.NewGuid().ToString("N"), "enabled", ExpectedVersion: expectedVersion, Enabled: enabled, Input: BehaviorWire.Null), cancellationToken);

    public Task<BehaviorSnapshot> RollbackAsync(string id, long version, long? expectedVersion = null, CancellationToken cancellationToken = default)
        => ChangeAsync(id, new(Guid.NewGuid().ToString("N"), "rollback", ExpectedVersion: expectedVersion, Version: version, Input: BehaviorWire.Null), cancellationToken);

    public async Task<BehaviorRunSnapshot> RunAsync(string id, JsonElement input, string? runId = null, CancellationToken cancellationToken = default)
    {
        runId ??= Guid.NewGuid().ToString("N");
        RequireId(runId);
        input = input.ValueKind == JsonValueKind.Undefined ? BehaviorWire.Null : input;
        var existing = await ReadRunAsync(id, runId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!JsonElement.DeepEquals(existing.Input, input))
            {
                throw new InvalidOperationException("This run id already belongs to different input. Use a new run id for a new request.");
            }
            return existing;
        }
        await ChangeAsync(id, new(Guid.NewGuid().ToString("N"), "run", RunId: runId,
            Input: input.ValueKind == JsonValueKind.Undefined ? BehaviorWire.Null : input), cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (await ReadRunAsync(id, runId, deadline.Token).ConfigureAwait(false) is { } run)
            {
                // Another caller may have admitted this id after our initial read.
                if (!JsonElement.DeepEquals(run.Input, input))
                {
                    throw new InvalidOperationException("This run id already belongs to different input. Use a new run id for a new request.");
                }
                return run;
            }
            await Task.Delay(30, deadline.Token).ConfigureAwait(false);
        }
    }

    public async Task<BehaviorRunSnapshot?> ReadRunAsync(string id, string runId, CancellationToken cancellationToken = default)
    {
        RequireId(id);
        RequireId(runId);
        cancellationToken.ThrowIfCancellationRequested();
        return await grains.GetGrain<IBehaviorRun>(RunNeuron(id, runId).Name).Read().ConfigureAwait(false);
    }

    public async Task<BehaviorRunSnapshot> WaitAsync(string id, string runId, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var run = await ReadRunAsync(id, runId, cancellationToken).ConfigureAwait(false);
            if (run is { Status: not "Running" })
            {
                return run;
            }
            await Task.Delay(60, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CancelAsync(string id, string runId, CancellationToken cancellationToken = default)
    {
        RequireId(id);
        RequireId(runId);
        var fired = await Source().Fire(Signal.Create("BehaviorCancel", "{}"), RunNeuron(id, runId), null, cancellationToken).ConfigureAwait(false);
        RequireAdmission(fired);
    }

    private async Task<BehaviorSnapshot> ChangeAsync(string id, BehaviorMutation change, CancellationToken cancellationToken)
    {
        RequireId(id);
        var signal = Signal.Create("BehaviorChange", BehaviorWire.Serialize(change));
        var fired = await Source().Fire(signal, new NeuronId("behavior", id), null, cancellationToken).ConfigureAwait(false);
        RequireAdmission(fired);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var snapshot = await ReadAsync(id, deadline.Token).ConfigureAwait(false);
            if (snapshot.Receipts.FirstOrDefault(receipt => receipt.OperationId == change.OperationId) is { } receipt)
            {
                if (receipt.Error is not null)
                {
                    throw new InvalidOperationException(receipt.Error);
                }
                return snapshot;
            }
            await Task.Delay(30, deadline.Token).ConfigureAwait(false);
        }
    }

    private static void RequireAdmission(FireOutcome outcome)
    {
        if (outcome.Busy > 0)
        {
            throw new NeuronBusyException("The program mailbox is full. Retry this operation after existing work finishes.");
        }
    }

    private INeuron Source() => grains.GetGrain<INeuron>(NeuronId.Plain("behavior-author").ToGrainId());
}
