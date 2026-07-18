namespace Ino.Core.Hosting;

/// <summary>
/// Per-user in-process event bus — the backbone of the gateway's <c>StreamEvents</c> surface
/// and the Flutter Trace view. Publishers push <see cref="InoEvent"/> entries keyed by a
/// user identifier; every live subscription for that user sees every subsequent publish.
/// Past events are NOT retained — subscriptions receive only events posted after
/// <see cref="SubscribeAsync"/> is called.
///
/// The v0.1 implementation is a singleton that lives in the gateway's process (system silo).
/// Synapse fires in other silos publish via the same interface resolved from their own DI;
/// a later slice turns this into a cross-silo bus if the inspector needs domains-silo
/// activity that isn't already observable via the gateway's Fire path.
/// </summary>
public interface IInoEventBus
{
    void Publish(string userId, InoEvent evt);

    IAsyncEnumerable<InoEvent> SubscribeAsync(string userId, CancellationToken ct);
}
