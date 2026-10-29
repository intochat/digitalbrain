using DigitalBrain.Capabilities;

namespace DigitalBrain;

/// <summary>
/// A module's grant to call one privileged member of a capability, declared on <see cref="ModuleManifest.Privileged"/>
/// (for example <c>new(Capability.Identity.V1, "Issue")</c>). The runtime refuses a privileged member to every module that
/// does not declare it (fail closed).
/// </summary>
/// <param name="Capability">The capability that owns the member.</param>
/// <param name="Member">The member name, for example <c>Issue</c>.</param>
public sealed record PrivilegedGrant(CapabilityId Capability, string Member);
