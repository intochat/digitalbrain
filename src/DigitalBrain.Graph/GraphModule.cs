namespace DigitalBrain.Graph;

/// <summary>The module that hosts the <c>brain.graph</c> neuron. It provides and requires no capability.</summary>
[Module("brain.graph")]
public sealed class GraphModule : Module
{
    /// <summary>What this module provides, requires and needs: nothing.</summary>
    public static ModuleManifest Manifest { get; } = new([], [], [], [], []);
}
