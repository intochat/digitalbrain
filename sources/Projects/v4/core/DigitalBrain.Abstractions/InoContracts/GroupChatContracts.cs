using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Ino;

[GenerateSerializer]
public sealed record GroupChatMessage(
    [property: Id(0)] string Sender, 
    [property: Id(1)] string Message) : Synapse;

[GenerateSerializer]
public sealed record GroupChatTranscriptUpdated(
    [property: Id(0)] Guid ChatId, 
    [property: Id(1)] string Sender, 
    [property: Id(2)] string Message, 
    [property: Id(3)] string FullTranscript) : Synapse;
