using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Cell;

// One compiled grain interprets many durable kinds. Identity is the key:
// owner/{kind}@{instance}. Built-in kinds ship with the palette; later
// waves load kind records from a registry without a new GrainType.
[GrainType(ICell.GrainTypeName)]
public sealed class CellNeuron : Neuron, ICell
{
    private const string StateName = "cell.state";
    private const char KindInstanceSeparator = '@';

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<CellState> _states;

    public CellNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<CellState>>();
    }

    public Task<CellSnapshot> Read()
        => Task.FromResult(SnapshotOf(LoadOrCreate()));

    public Task HandleAsync(CellApply synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (string.IsNullOrWhiteSpace(synapse.Key))
        {
            throw new NeuronAuthorizationException(
                $"Cell '{Id}' refuses an empty key. Send a digit, operator, '=', 'C', 'CE', or 'BS'.");
        }

        var identity = ParseIdentity();
        var kind = ResolveKind(identity.Kind);
        var state = LoadOrCreate();
        if (!string.Equals(state.Kind, identity.Kind, StringComparison.Ordinal))
        {
            state = CellState.Fresh(identity.Kind, identity.Instance);
        }

        state = kind.Apply(state, synapse.Key.Trim());
        Stage(state);

        return ReplyAsync(SnapshotOf(state), cancellationToken);
    }

    public Task HandleAsync(CellReset synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var identity = ParseIdentity();
        var state = CellState.Fresh(identity.Kind, identity.Instance);
        Stage(state);
        return ReplyAsync(SnapshotOf(state), cancellationToken);
    }

    private (string Kind, string Instance) ParseIdentity()
    {
        var name = Id.Name;
        var separator = name.IndexOf(KindInstanceSeparator, StringComparison.Ordinal);
        if (separator <= 0 || separator == name.Length - 1)
        {
            throw new NeuronAuthorizationException(
                $"Cell '{Id}' requires a key of the form kind@instance "
                + $"(got '{name}'). Example: calculator@main.");
        }

        return (name[..separator], name[(separator + 1)..]);
    }

    private static ICellKind ResolveKind(string kind)
    {
        if (string.Equals(kind, CalculatorKind.KindName, StringComparison.OrdinalIgnoreCase))
        {
            return CalculatorKind.Instance;
        }

        throw new NeuronAuthorizationException(
            $"Cell kind '{kind}' is not installed. Built-in kinds: {CalculatorKind.KindName}. "
            + "Install a kind record (later wave) or use calculator@{{name}}.");
    }

    private CellState LoadOrCreate()
    {
        if (_state.Value is { Length: > 0 } serialized)
        {
            return _states.Deserialize(serialized);
        }

        var identity = ParseIdentity();
        _ = ResolveKind(identity.Kind);
        return CellState.Fresh(identity.Kind, identity.Instance);
    }

    private void Stage(CellState data) => _state.Value = _states.SerializeToArray(data);

    private CellSnapshot SnapshotOf(CellState state)
        => new(state.Kind, state.Instance, state.Display, state.Value, state.Phase);

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A cell command requires a command id.");
        }
    }
}
