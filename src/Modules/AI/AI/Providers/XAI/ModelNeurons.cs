using Orleans.Runtime;

namespace DigitalBrain.AI;

[GrainType("ai.llm.grok46")]
internal sealed class Grok46Neuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.XAI.IGrok46)), DigitalBrain.AI.XAI.IGrok46;