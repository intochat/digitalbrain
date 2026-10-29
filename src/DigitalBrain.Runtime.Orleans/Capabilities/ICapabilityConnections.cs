using DigitalBrain.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>One connection of a brain as the host lists it.</summary>
/// <param name="Key">The stable connection key in the brain's Settings.</param>
/// <param name="Label">The display label.</param>
/// <param name="Connected">False while the connection is pending or broken; the broker skips it.</param>
/// <param name="Provides">The capabilities the connection's module provides.</param>
/// <param name="Root">The <c>ItemId</c> prefix owned by this connection, or null when it owns none.</param>
public sealed record BrainConnection(string Key, string Label, bool Connected, IReadOnlyList<CapabilityId> Provides, string? Root);

/// <summary>
/// The host SPI behind <see cref="CapabilityBroker"/> (R4-02, D-10): the os kernel implements it over its connection requests.
/// The runtime never reads Settings itself.
/// </summary>
public interface ICapabilityConnections
{
    /// <summary>Every connection of <paramref name="brain"/>, in the brain's Settings order.</summary>
    Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct);

    /// <summary>Binds connection <paramref name="key"/> as <typeparamref name="TCapability"/> for <paramref name="callerApp"/> (audit, CAP-05).</summary>
    Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class;

    /// <summary>Raises a request that the brain's owner connect something providing <paramref name="capability"/>; hosts dedupe.</summary>
    Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct);
}

/// <summary>
/// Wraps every connection the broker binds before it reaches the caller. 07-10's storage access recording decorator plugs in
/// here; interceptors run in registration order.
/// </summary>
public interface ICapabilityBindingInterceptor
{
    /// <summary>Returns <paramref name="capability"/> or a wrapper around it.</summary>
    TCapability Wrap<TCapability>(BrainId brain, BrainConnection connection, string callerApp, TCapability capability) where TCapability : class;
}
