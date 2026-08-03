using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.transcript-read")]
[Description("A conversation's durable transcript, answering a chat.read-transcript-request")]
public sealed record TranscriptRead(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] ChatTranscript Transcript) : Synapse;
