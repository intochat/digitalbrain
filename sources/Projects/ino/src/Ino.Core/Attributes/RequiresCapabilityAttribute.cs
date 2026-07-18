namespace Ino.Core;

/// <summary>
/// Declares a capability required by a neuron. Aggregated by the Phase 3 source generator
/// into DomainMetadata.RequiredCapabilities and surfaced at install time via the
/// marketplace consent screen.
///
/// Usage:
///   [RequiresCapability(typeof(Capability.Http), "serpapi.com")]
///   [RequiresCapability(typeof(Capability.Llm), LlmTier.Reasoning)]
///   public sealed class TripPlanner : Neuron&lt;TripPlannerEvent&gt;, ...
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresCapabilityAttribute : Attribute
{
    public RequiresCapabilityAttribute(Type capabilityType, params object?[] args)
    {
        CapabilityType = capabilityType;
        Args = args;
    }

    public Type CapabilityType { get; }

    public IReadOnlyList<object?> Args { get; }
}
