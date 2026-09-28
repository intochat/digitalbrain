namespace DigitalBrain.AI;

[Alias("ai.llm"), Orleans.Metadata.DefaultGrainType("ai.llm")]
public interface ILLM : IAiMarker, DigitalBrain.Contracts.INeuron
{
    Task<ModelDescriptor> Describe(AgentModelSelection? selection = null);
    [ResponseTimeout("00:10:00")] Task<InferenceResult> Generate(InferenceRequest request, CancellationToken cancellationToken = default);
    [ResponseTimeout("00:10:00")] IAsyncEnumerable<InferenceUpdate> GenerateStreaming(InferenceRequest request, CancellationToken cancellationToken = default);
}