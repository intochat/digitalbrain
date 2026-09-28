using Orleans.Runtime;

namespace DigitalBrain.AI;

[GrainType("ai.llm.gpt54")]
internal sealed class Gpt54Neuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.OpenAI.IGpt54)), DigitalBrain.AI.OpenAI.IGpt54;

[GrainType("ai.llm.gpt54mini")]
internal sealed class Gpt54MiniNeuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.OpenAI.IGpt54Mini)), DigitalBrain.AI.OpenAI.IGpt54Mini;

[GrainType("ai.llm.gpt54nano")]
internal sealed class Gpt54NanoNeuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.OpenAI.IGpt54Nano)), DigitalBrain.AI.OpenAI.IGpt54Nano;

[GrainType("ai.llm.gpt56luna")]
internal sealed class Gpt56LunaNeuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.OpenAI.IGpt56Luna)), DigitalBrain.AI.OpenAI.IGpt56Luna;

[GrainType("ai.llm.gpt56sol")]
internal sealed class Gpt56SolNeuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.OpenAI.IGpt56Sol)), DigitalBrain.AI.OpenAI.IGpt56Sol;

[GrainType("ai.llm.gpt56terra")]
internal sealed class Gpt56TerraNeuron(InferenceService inference) : LlmNeuronBase(inference, typeof(DigitalBrain.AI.OpenAI.IGpt56Terra)), DigitalBrain.AI.OpenAI.IGpt56Terra;