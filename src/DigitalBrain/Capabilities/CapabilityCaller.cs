namespace DigitalBrain.Capabilities;

/// <summary>
/// The caller a host stamped on the current call, as a capability port receives it (D-15). Capability contracts take no
/// caller; the runtime reads the stamped caller and passes it explicitly to the port that implements the capability.
/// </summary>
/// <param name="PrincipalId">The calling principal.</param>
/// <param name="AccountId">The account paying for the call.</param>
/// <param name="BrainId">The brain (workspace) the call runs in.</param>
/// <param name="AppId">The app making the call, or null for a user.</param>
/// <param name="ConversationId">The conversation an app call runs for, if any.</param>
public sealed record CapabilityCaller(string PrincipalId, string AccountId, string BrainId, string? AppId = null, string? ConversationId = null);
