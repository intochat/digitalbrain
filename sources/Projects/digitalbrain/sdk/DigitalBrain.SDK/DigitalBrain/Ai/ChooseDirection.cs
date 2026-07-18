using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

// Fired when the user taps a chip on the OptionChipStackCard. The CorrelationId
// references the original BrainstormOptions so downstream neurons can rejoin
// the chain (e.g. ConveneRequest to GroupChatNeuron with the chosen direction's
// Participants).
[GenerateSerializer]
public sealed record ChooseDirectionRequest([property: Id(1)] string ChosenOptionId,
    [property: Id(2)] string ChosenOptionTitle,
    [property: Id(3)] string OriginalPrompt,
    [property: Id(4)] IReadOnlyList<string> Participants
) : Synapse;
