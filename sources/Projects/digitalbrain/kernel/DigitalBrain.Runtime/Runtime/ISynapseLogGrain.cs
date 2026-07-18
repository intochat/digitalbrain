namespace DigitalBrain.Runtime.Runtime;

// E-RUN #37. Durable broadcast buffer for fan-out signals — every signal the
// Cortex routes is appended here before being delivered to subscribers, so
// BrainWatch and offline replay both see the full broadcast tape.
//
// Single global instance keyed by Guid.Empty, mirroring IBrainCatalog: there
// is one signal pipeline per cluster, not one per neuron. The proto used
// IntegerKey(0); GuidKey(Guid.Empty) matches the rest of the kernel grains
// and avoids the "what does key 0 mean" question for future maintainers.
public interface ISynapseLogGrain : IGrainWithGuidKey
{
    Task AppendAsync(SynapseEnvelope envelope);

    Task<IReadOnlyList<SynapseEnvelope>> AllAsync();
}
