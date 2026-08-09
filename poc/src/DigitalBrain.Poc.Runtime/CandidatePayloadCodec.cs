using DigitalBrain.Poc.Abstractions;
using Orleans.Serialization;

namespace DigitalBrain.Poc.Runtime;

internal sealed class CandidatePayloadCodec
{
    internal const string OrleansObjectSerializerFormat = "orleans-object-serializer";

    private readonly ObjectSerializer _serializer;
    private readonly LoadedCandidate _candidate;

    public CandidatePayloadCodec(ObjectSerializer serializer, LoadedCandidate candidate)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    }

    public byte[] Serialize(SynapseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidatePinnedIdentity(envelope.TargetModuleIdentity);
        var handler = ResolveManifestBoundHandler(envelope.ContractAlias, envelope.Synapse.GetType());
        using var stream = new MemoryStream();
        _serializer.Serialize(envelope.Synapse, stream, handler.SynapseType, sizeHint: 256);
        return stream.ToArray();
    }

    public Synapse Deserialize(PendingOutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(
                envelope.PayloadFormat,
                OrleansObjectSerializerFormat,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Committed candidate payload '{envelope.DeliveryId}' does not use the configured Orleans serializer.");
        }

        ValidatePinnedIdentity(envelope.TargetModuleIdentity);
        var handler = ResolveManifestBoundHandler(envelope.ContractAlias, expectedType: null);
        var payload = Convert.FromBase64String(envelope.PayloadBase64);
        using var stream = new MemoryStream(payload, writable: false);
        var restored = _serializer.Deserialize(stream, handler.SynapseType);
        if (restored is not Synapse synapse || synapse.GetType() != handler.SynapseType)
        {
            throw new InvalidDataException(
                $"Committed outbox payload '{envelope.DeliveryId}' did not restore its exact manifest-bound synapse type.");
        }

        return synapse;
    }

    private ExactHandler ResolveManifestBoundHandler(string contractAlias, Type? expectedType)
    {
        var handlers = _candidate.Catalog.Resolve(contractAlias)
            .Where(handler => expectedType is null || handler.SynapseType == expectedType)
            .Where(handler => _candidate.GrantedCandidateOutputTypes.Contains(handler.SynapseType))
            .ToArray();
        return handlers.Length == 1
            ? handlers[0]
            : throw new InvalidDataException(
                $"Candidate payload alias '{contractAlias}' does not select one manifest-bound output type.");
    }

    private void ValidatePinnedIdentity(CandidateModuleIdentity? identity)
    {
        if (identity is null || identity != _candidate.Identity)
        {
            throw new InvalidDataException(
                "Candidate payload module identity does not match the loaded immutable module identity.");
        }
    }
}
