using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.NemoChat;

[GenerateSerializer]
public sealed record NemoChatRequest([property: Id(0)] string Prompt) : Synapse;
