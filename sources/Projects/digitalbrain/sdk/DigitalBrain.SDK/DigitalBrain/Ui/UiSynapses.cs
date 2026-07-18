using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ui;

/// <summary>
/// Dictates how the active cards on screen should be arranged by the shell.
/// </summary>
[GenerateSerializer]
public sealed record LayoutRequest(
    [property: Id(1)] string LayoutMode, // "swarm" | "split" | "grid" | "focus" | "modal"
    [property: Id(2)] string[] ActiveNeuronFqns
) : Synapse;

/// <summary>
/// Commands the shell's 3D camera to transition/orbit to focus on a target neuron.
/// </summary>
[GenerateSerializer]
public sealed record NavigateRequest(
    [property: Id(1)] string TargetNeuronFqn,
    [property: Id(2)] string TransitionStyle, // "zoom" | "pan" | "fade"
    [property: Id(3)] double DurationSeconds = 1.2
) : Synapse;

/// <summary>
/// Forces a specific neuron's card to pin to a designated viewport zone.
/// </summary>
[GenerateSerializer]
public sealed record PositionRequest(
    [property: Id(1)] string NeuronFqn,
    [property: Id(2)] string PositionSlot, // "dock" | "left-panel" | "right-panel" | "floating"
    [property: Id(3)] double? X = null,
    [property: Id(4)] double? Y = null
) : Synapse;

/// <summary>
/// Configures premium ambient filters, glassmorphic blur intensity, or edge glows.
/// </summary>
[GenerateSerializer]
public sealed record VisualStateRequest(
    [property: Id(1)] string ThemePreset, // "liquid_glass" | "monochrome" | "nebula"
    [property: Id(2)] double BlurSigma,
    [property: Id(3)] bool EdgeGlowActive,
    [property: Id(4)] string AccentColorHex
) : Synapse;
