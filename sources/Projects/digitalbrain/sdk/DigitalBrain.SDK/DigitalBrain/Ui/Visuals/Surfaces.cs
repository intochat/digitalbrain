namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

public static class Surfaces
{
    public const string GlassSheet        = "glass.sheet";
    public const string GlassDialog       = "glass.dialog";
    public const string GlassSideSheet    = "glass.sideSheet";
    public const string GlassFloatingCard = "glass.floatingCard";
    public const string GlowTaskRow       = "glow.taskRow";
    public const string GlowNeuronLabel   = "glow.neuronLabel";
    public const string GlowCometHead     = "glow.cometHead";

    public static readonly IReadOnlyList<string> All =
    [
        GlassSheet, GlassDialog, GlassSideSheet, GlassFloatingCard,
        GlowTaskRow, GlowNeuronLabel, GlowCometHead,
    ];
}
