namespace DigitalBrain.Capabilities;

/// <summary>Resolves the connections that implement a capability for a brain.</summary>
public interface ICapabilityBroker
{
    /// <summary>The implementation of <typeparamref name="TCapability"/> for a single-connection capability (secrets, identity).</summary>
    /// <exception cref="InvalidOperationException">The brain has no connection, or more than one, for the capability; use <see cref="ResolveAll{TCapability}"/>.</exception>
    Task<TCapability> Resolve<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class;

    /// <summary>Every connection of the brain that implements <typeparamref name="TCapability"/>, in Settings order (storage is many-connection).</summary>
    Task<IReadOnlyList<CapabilityConnection<TCapability>>> ResolveAll<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class;
}

/// <summary>One connection of a brain that implements a capability.</summary>
/// <param name="ConnectionKey">The stable key of the connection in the brain's Settings.</param>
/// <param name="Label">The display label of the connection.</param>
/// <param name="Capability">The implementation bound to the connection.</param>
public sealed record CapabilityConnection<TCapability>(string ConnectionKey, string Label, TCapability Capability) where TCapability : class;
