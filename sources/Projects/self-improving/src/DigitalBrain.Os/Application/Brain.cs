namespace DigitalBrain.Os.Application;

// Self-explanatory singleton key for the main orchestrator grain (IDigitalBrain + IAspire activations use this).
// Promoted/ restored here as part of Core primitive boundary cleanup.
public static class Brain
{
    public const string WellKnownKey = "global";
}
