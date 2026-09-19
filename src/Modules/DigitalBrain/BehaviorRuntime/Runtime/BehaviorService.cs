using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scenarios;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core.Behavior;

public sealed partial class BehaviorService(IGrainFactory grains, INeuronInvoker invoker, IServiceProvider services)
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

    internal static NeuronId NodeNeuron(string behaviorId, string nodeId) => new("behavior-node", $"{behaviorId}/{nodeId}");

    public async Task<IReadOnlyList<BehaviorSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureDefaultsAsync(cancellationToken).ConfigureAwait(false);
        var ids = await grains.GetGrain<IBehaviorIndex>("all").List().ConfigureAwait(false);
        return await Task.WhenAll(ids.Select(id => ReadAsync(id, cancellationToken))).ConfigureAwait(false);
    }

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IBehaviorDefaults>() is not { Definitions: { Count: > 0 } definitions })
        {
            return;
        }

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await ReadAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            try
            {
                if (current.Definition is null)
                {
                    await DeployAsync(definition, current.Version, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WireAsync(current, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                // Another caller deployed the same default, or a sample cannot bind yet.
            }
        }
    }

    public Task<BehaviorSnapshot> ReadAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireId(id);
        return grains.GetGrain<IBehavior>(id).Read();
    }

    public async Task<BehaviorSnapshot> DeployAsync(BehaviorDefinition definition, long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        RequireId(definition.Id);
        var validation = Validate(definition);
        if (!validation.Valid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors));
        }

        var current = await ReadAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        var revision = new BehaviorRevision(current.Version + 1, definition, DateTimeOffset.UtcNow);
        var snapshot = await grains.GetGrain<IBehavior>(definition.Id).Write(current with
        {
            Version = revision.Version,
            Enabled = true,
            Definition = definition,
            Versions = [.. current.Versions.TakeLast(49), revision],
        }, expectedVersion).ConfigureAwait(false);
        await grains.GetGrain<IBehaviorIndex>("all").Remember(definition.Id).ConfigureAwait(false);
        await WireAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<BehaviorSnapshot> SetEnabledAsync(string id, bool enabled, long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(id, cancellationToken).ConfigureAwait(false);
        if (current.Definition is null)
        {
            throw new ArgumentException("Deploy the behavior before changing its state.");
        }

        var revision = new BehaviorRevision(current.Version + 1, current.Definition, DateTimeOffset.UtcNow);
        return await grains.GetGrain<IBehavior>(id).Write(current with
        {
            Enabled = enabled,
            Version = revision.Version,
            Versions = [.. current.Versions.TakeLast(49), revision],
        }, expectedVersion).ConfigureAwait(false);
    }

    public async Task<BehaviorSnapshot> RollbackAsync(string id, long version, long? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(id, cancellationToken).ConfigureAwait(false);
        var definition = current.Versions.FirstOrDefault(item => item.Version == version)?.Definition
            ?? throw new ArgumentException("The requested behavior definition or revision does not exist.");
        return await DeployAsync(definition, expectedVersion, cancellationToken).ConfigureAwait(false);
    }

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

        var snapshot = await ReadAsync(id, cancellationToken).ConfigureAwait(false);
        if (snapshot.Definition is null)
        {
            throw new ArgumentException("Deploy the behavior before running it.");
        }
        if (!snapshot.Enabled)
        {
            throw new InvalidOperationException("This behavior is paused. Enable it before running.");
        }

        var admitted = new BehaviorRunSnapshot(runId, id, snapshot.Version, "Running", input, BehaviorWire.Null,
            [], null, DateTimeOffset.UtcNow, null);
        await grains.GetGrain<IBehavior>(id).RecordRun(admitted).ConfigureAwait(false);

        var body = GraphEnvelope.Write(runId, input, input, new Dictionary<string, JsonElement>(StringComparer.Ordinal), "input");
        var correlation = Correlation(runId);
        foreach (var node in snapshot.Definition.Nodes.Where(node => node.Kind.Equals("input", StringComparison.OrdinalIgnoreCase)))
        {
            var signal = Signal.Create(snapshot.Definition.Trigger, body);
            var delivery = SignalDelivery.Create(signal, grains.GetGrain<INeuron>(NeuronId.Plain("behavior-author").ToGrainId()), 1, TimeProvider.System, correlation: correlation);
            var admission = await grains.GetGrain<INeuron>(NodeNeuron(id, node.Id).ToGrainId())
                .Deliver(delivery, cancellationToken).ConfigureAwait(false);
            if (admission == DeliveryAdmission.Busy)
            {
                throw new NeuronBusyException("The behavior mailbox is full. Retry this operation after existing work finishes.");
            }
        }

        return admitted;
    }

    public Task<BehaviorRunSnapshot?> ReadRunAsync(string id, string runId, CancellationToken cancellationToken = default)
    {
        RequireId(id);
        RequireId(runId);
        cancellationToken.ThrowIfCancellationRequested();
        return grains.GetGrain<IBehavior>(id).ReadRun(runId);
    }

    public async Task CancelAsync(string id, string runId, CancellationToken cancellationToken = default)
    {
        RequireId(id);
        RequireId(runId);
        cancellationToken.ThrowIfCancellationRequested();
        services.GetService<IBehaviorRunCancellation>()?.Cancel(id, runId);
        var current = await grains.GetGrain<IBehavior>(id).ReadRun(runId).ConfigureAwait(false);
        if (current is { Status: "Running" })
        {
            await grains.GetGrain<IBehavior>(id).RecordRun(current with
            {
                Status = "Cancelled",
                CompletedAt = DateTimeOffset.UtcNow,
                Error = "The run was cancelled.",
            }).ConfigureAwait(false);
        }
    }

    private async Task WireAsync(BehaviorSnapshot snapshot, CancellationToken cancellationToken)
    {
        var definition = snapshot.Definition ?? throw new ArgumentException("A deploy needs a definition.");
        foreach (var node in definition.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var predecessors = definition.Synapses.Where(edge => edge.To == node.Id).Select(edge => edge.From).Distinct().ToArray();
            await grains.GetGrain<IBehaviorNode>(NodeNeuron(snapshot.Id, node.Id).ToGrainId())
                .Configure(snapshot.Id, snapshot.Version, node, definition.Trigger, predecessors).ConfigureAwait(false);
        }

        foreach (var edge in definition.Synapses)
        {
            await grains.GetGrain<IScenario>(DigitalBrainNames.DefaultScenario)
                .Bind(
                    grains.GetGrain<INeuron>(NodeNeuron(snapshot.Id, edge.From).ToGrainId()),
                    BehaviorNodeNeuron.ValueType,
                    grains.GetGrain<INeuron>(NodeNeuron(snapshot.Id, edge.To).ToGrainId()))
                .ConfigureAwait(false);
        }
    }

    internal static CorrelationId Correlation(string runId)
    {
        if (Guid.TryParseExact(runId, "N", out var value) && value != Guid.Empty)
        {
            return new(value);
        }

        return new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(runId)).AsSpan(0, 16)));
    }

    private INeuron Source() => grains.GetGrain<INeuron>(NeuronId.Plain("behavior-author").ToGrainId());
}
