namespace DigitalBrain.Runtime.Runtime;

public interface IMemoryEventLogGrain : IGrainWithGuidKey
{
    Task AppendAsync(SynapseEnvelope envelope);
    Task<IReadOnlyList<SynapseEnvelope>> QueryAsync(DateTimeOffset? since, int? limit);
    Task ConfigureDecayPolicyAsync(DecayPolicy policy);
}
