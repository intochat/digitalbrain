using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Google.Gmail", "DigitalBrain.Google.GmailGetMessageRequest", true),
        ("DigitalBrain.Google.Gmail", "DigitalBrain.Google.GmailGetMessageResponse", false),
        ("DigitalBrain.Google.Gmail", "DigitalBrain.Google.GmailRequest", true),
        ("DigitalBrain.Google.Gmail", "DigitalBrain.Google.GmailResponse", false),
        ("DigitalBrain.Google.Gmail", "DigitalBrain.Google.GmailSearchRequest", true),
        ("DigitalBrain.Google.Gmail", "DigitalBrain.Google.GmailSearchResponse", false),
    ];
}
