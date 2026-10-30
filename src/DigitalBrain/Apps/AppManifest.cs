using DigitalBrain.Capabilities;

namespace DigitalBrain;

/// <summary>What an app is and what it requires.</summary>
/// <param name="Id">The frozen app id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Version">The app version.</param>
/// <param name="Neurons">The neuron classes the app brings.</param>
/// <param name="Requires">What the app requires from the brain.</param>
public sealed record AppManifest(string Id, string Name, string Version, IReadOnlyList<Type> Neurons, AppRequirements Requires)
{
    /// <summary>Optional hex accent color (<c>#RRGGBB</c>) for the app's squircle icon badge.</summary>
    public string? Accent { get; init; }

    /// <summary>Optional app category label (e.g., <c>Intelligence</c>, <c>Workspace</c>, <c>Studio</c>).</summary>
    public string? Category { get; init; }
}

/// <summary>What an app requires from the brain. A requirement is a need, not a grant.</summary>
/// <param name="Capabilities">The capabilities the app calls; each at most once.</param>
public sealed record AppRequirements(IReadOnlyList<CapabilityId> Capabilities)
{
    /// <summary>The capabilities the app calls; each at most once.</summary>
    /// <exception cref="InvalidDataException">A capability is listed twice.</exception>
    public IReadOnlyList<CapabilityId> Capabilities { get; init => field = ModuleManifest.RejectDuplicates(value, "App requirements"); } = ModuleManifest.RejectDuplicates(Capabilities, "App requirements");
}
