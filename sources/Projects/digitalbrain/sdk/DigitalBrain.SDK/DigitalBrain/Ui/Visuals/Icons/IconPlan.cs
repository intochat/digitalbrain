using DigitalBrain.Runtime.Neurons;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals.Icons;

public static class IconPlan
{
    public const string CardLibrary   = "digitalbrain";
    public const string CardRootWidget = "IconSpecCard";

    static readonly string[] Shapes = ["orb", "hex", "leaf", "lens"];

    static readonly Dictionary<string, string> DomainTone = new(StringComparer.OrdinalIgnoreCase)
    {
        ["data"]        = "teal",
        ["sqlite"]      = "teal",
        ["ai"]          = "violet",
        ["google"]      = "gold",
        ["travel"]      = "rose",
        ["dynamic"]     = "indigo",
        ["onboarding"]  = "indigo",
        ["canvas"]      = "indigo",
        ["visuals"]     = "indigo",
        ["kernel"]      = "indigo",
        ["engineering"] = "indigo",
    };

    public static uint Seed(string neuronFqn)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(neuronFqn));
        return BitConverter.ToUInt32(bytes, 0);
    }

    public static string ShapeHintFor(uint seed) => Shapes[(int)(seed % (uint)Shapes.Length)];

    public static string ToneFor(string neuronFqn)
    {
        var parts = neuronFqn.Split('.', '/');
        foreach (var p in parts)
            if (DomainTone.TryGetValue(p, out var tone)) return tone;
        return "indigo";
    }

    public static IconSpecResolved Resolve(
        ResolveIconSpec req,
        IconOverride? overrideRecord,
        Guid callerNeuronId,
        string callerNeuronType,
        DateTimeOffset timestamp)
    {
        var seed  = Seed(req.NeuronFqn);
        var shape = overrideRecord?.ShapeHint ?? ShapeHintFor(seed);
        var tone  = overrideRecord?.Tone      ?? ToneFor(req.NeuronFqn);
        return new IconSpecResolved(NeuronFqn:          req.NeuronFqn,
        Seed:               seed,
        Tone:               tone,
        ShapeHint:          shape,
        OverrideAssetKey:   overrideRecord?.OverrideAssetKey) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: callerNeuronId,
            callerNeuronType: callerNeuronType,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: timestamp
        ) };
    }

    public static IconSpecCardPayload ToCardPayload(IconSpecResolved resolved) =>
        new(resolved.NeuronFqn, resolved.Seed, resolved.Tone, resolved.ShapeHint);

    public static IconOverride NewOverrideRecord(SetIconOverride req, DateTimeOffset utc) =>
        new(req.NeuronFqn, req.Tone, req.ShapeHint, req.OverrideAssetKey, utc);
}
