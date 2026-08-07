using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Shell.SceneNeuron", "DigitalBrain.Shell.ControlActivated", true),
        ("DigitalBrain.Shell.ShellBootNeuron", "DigitalBrain.Abstractions.DigitalBrainActivated", true),
        ("DigitalBrain.Shell.ShellNeuron", "DigitalBrain.Shell.OpenScene", true),
        ("DigitalBrain.Shell.ShellNeuron", "DigitalBrain.Shell.SceneOpened", false),
    ];
}
