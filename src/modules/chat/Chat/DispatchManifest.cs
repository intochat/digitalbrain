using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Chat.ChatNeuron", "DigitalBrain.Chat.AssistantResponded", false),
        ("DigitalBrain.Chat.ChatNeuron", "DigitalBrain.Chat.ReadTranscriptRequest", true),
        ("DigitalBrain.Chat.ChatNeuron", "DigitalBrain.Chat.TranscriptRead", false),
        ("DigitalBrain.Chat.ChatNeuron", "DigitalBrain.Chat.UserMessaged", false),
    ];
}
