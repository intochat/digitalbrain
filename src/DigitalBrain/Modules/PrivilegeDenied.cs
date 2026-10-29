using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Capabilities;

namespace DigitalBrain;

/// <summary>
/// A privileged capability member was called by a module whose manifest does not grant it (or by a caller with no module).
/// The message names the module, capability and member only.
/// </summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract error names.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The refusal carries a module, capability and member only.")]
public sealed class PrivilegeDenied(string? moduleId, CapabilityId capability, string member)
    : UnauthorizedAccessException($"{capability}.{member} is privileged; module '{moduleId ?? "(none)"}' has no grant for it in its manifest.")
{
    /// <summary>The calling module, or null when the caller is not a module.</summary>
    public string? ModuleId { get; } = moduleId;

    /// <summary>The capability.</summary>
    public CapabilityId Capability { get; } = capability;

    /// <summary>The member refused.</summary>
    public string Member { get; } = member;
}
