using System.Text.Json;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GrainType(IKindRegistry.GrainTypeName)]
public sealed class KindRegistryNeuron : Neuron, IKindRegistry
{
    private const string StateName = "kinds.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly KindRecord CalculatorBuiltin = new(
        "calculator",
        "Calculator",
        "Closed total evaluator: digits, ops, equals.",
        ["0-9", ".", "+", "-", "*", "/", "=", "C", "CE", "BS"],
        DateTimeOffset.UnixEpoch,
        Builtin: true);

    private readonly IDurableValue<string> _json;

    public KindRegistryNeuron()
    {
        _json = ServiceProvider.GetRequiredKeyedService<IDurableValue<string>>(StateName);
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

        _json.Value = JsonSerializer.Serialize(all.ToArray(), JsonOptions);
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
    {
        var text = _json.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return JsonSerializer.Deserialize<KindRecord[]>(text, JsonOptions) ?? [];
    }
}
