using Orleans.Runtime;

namespace DigitalBrain.AI;

[GrainType("ai.llm.deepseek-v41-flash")]
internal sealed class DeepSeekV41FlashNeuron(InferenceService inference)
    : LlmNeuronBase(inference, typeof(DigitalBrain.AI.OpenRouter.IDeepSeekV41Flash)), DigitalBrain.AI.OpenRouter.IDeepSeekV41Flash;
