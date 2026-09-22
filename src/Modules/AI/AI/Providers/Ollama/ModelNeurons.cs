using Orleans.Runtime;

namespace DigitalBrain.AI;

[GrainType("ai.llm.gemma4")]
internal sealed class Gemma4Neuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.Ollama.IGemma4)), DigitalBrain.AI.Ollama.IGemma4;

[GrainType("ai.llm.qwen35")]
internal sealed class Qwen35Neuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.Ollama.IQwen35)), DigitalBrain.AI.Ollama.IQwen35;