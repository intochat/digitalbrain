using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Kinds;

[GrainType(IKindRegistry.GrainTypeName)]
public sealed class KindRegistryNeuron : Neuron, IKindRegistry
{
    private const string StateName = "kinds.state";

    private static readonly KindRecord CalculatorBuiltin = new(
        "calculator",
        "Calculator",
        "Closed total evaluator: digits, ops, equals.",
        ["0-9", ".", "+", "-", "*", "/", "=", "C", "CE", "BS"],
        DateTimeOffset.UnixEpoch,
        Builtin: true);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<KindRegistryState> _states;

    public KindRegistryNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<KindRegistryState>>();
    }

    public Task HandleAsync(InstallKind synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        if (synapse.CommandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("InstallKind requires a command id.");
        }

        if (string.IsNullOrWhiteSpace(synapse.Kind) || string.IsNullOrWhiteSpace(synapse.DisplayName))
        {
            throw new NeuronAuthorizationException("Kind and DisplayName are required.");
        }

        var kind = synapse.Kind.Trim().ToLowerInvariant();
        if (string.Equals(kind, CalculatorBuiltin.Kind, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException(
                "calculator is a built-in kind and cannot be reinstalled.");
        }

        var record = new KindRecord(
            kind,
            synapse.DisplayName.Trim(),
            string.IsNullOrWhiteSpace(synapse.Description) ? null : synapse.Description.Trim(),
            synapse.AcceptedKeys ?? [],
            TimeProvider.GetUtcNow(),
            Builtin: false);

        var all = LoadInstalled().ToList();
        var index = all.FindIndex(k => string.Equals(k.Kind, kind, StringComparison.Ordinal));
        if (index >= 0)
        {
            all[index] = record;
        }
        else
        {
            all.Add(record);
        }

        Stage(new KindRegistryState([.. all]));
        return ReplyAsync(new KindInstalled(synapse.CommandId, record), cancellationToken);
    }

    public Task HandleAsync(ListKinds synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        KindRecord[] kinds =
        [
            CalculatorBuiltin,
            .. LoadInstalled().OrderBy(k => k.Kind, StringComparer.Ordinal),
        ];
        return ReplyAsync(new KindsListed(synapse.CommandId, kinds), cancellationToken);
    }

    private KindRecord[] LoadInstalled()
        => Load().Installed;

    private KindRegistryState Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : KindRegistryState.Empty;

    private void Stage(KindRegistryState state)
        => _state.Value = _states.SerializeToArray(state);
}
