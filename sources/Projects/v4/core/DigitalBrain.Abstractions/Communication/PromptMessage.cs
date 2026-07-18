using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Communication;

[GenerateSerializer]
public record PromptMessage([property: Id(0)] string Prompt) : Synapse;
