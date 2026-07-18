using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.User;

[GenerateSerializer]
public sealed record UserPromptReceived([property: Id(1)] string UserId,
    [property: Id(2)] string Text
) : Synapse;
