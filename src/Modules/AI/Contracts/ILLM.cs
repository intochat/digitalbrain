namespace DigitalBrain.AI;

// Marker root for LLM model markers; models are consumed exclusively through
// keyed IChatClient services that agents select with [Llm<TModel>].
public interface ILLM : IAiMarker;
