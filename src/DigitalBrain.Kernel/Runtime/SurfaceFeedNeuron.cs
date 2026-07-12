using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.runtime.surface-feed.v1")]
public sealed class SurfaceFeedNeuron(
    [PersistentState("surface-feed", RuntimeStateStorageProviders.SurfaceFeeds)]
    IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector) : Grain, ISurfaceFeedNeuron
{
    private EncryptedPersistentState<SurfaceFeedState>? _state;

    private EncryptedPersistentState<SurfaceFeedState> State => _state ??= new(
        persistentState,
        protector,
        this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Surface-feed grains require a string key."),
        RuntimeStateKinds.SurfaceFeed,
        RuntimeStateSchemas.SurfaceFeed,
        SurfaceFeedState.Empty,
        static value => value.Revision,
        SurfaceFeedTransitions.Validate);

    public Task<SurfaceFeedState> ReadAsync() => State.ReadAsync();

    public Task<SurfaceFeedState> InitializeAsync(long expectedRevision, SurfaceFeedIdentity identity) =>
        State.UpdateAsync(expectedRevision, current =>
            SurfaceFeedTransitions.Initialize(current, expectedRevision, identity));

    public Task<SurfaceFeedState> EnsureHomeSurfaceAsync(long expectedRevision, HomeSurfaceBootstrap bootstrap) =>
        State.UpdateAsync(expectedRevision, current =>
            SurfaceFeedTransitions.EnsureHomeSurface(current, expectedRevision, bootstrap));

    public Task<SurfaceFeedState> ApplyProjectionAsync(
        long expectedRevision,
        SurfaceFeedProjection projection,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current =>
            SurfaceFeedTransitions.ApplyProjection(current, expectedRevision, projection, now));

    public Task<SurfaceFeedState> RecordDeliveryAsync(
        long expectedRevision,
        string deliveryId,
        long sequence,
        DateTimeOffset deliveredAt) =>
        State.UpdateAsync(expectedRevision, current => SurfaceFeedTransitions.RecordDelivery(
            current,
            expectedRevision,
            deliveryId,
            sequence,
            deliveredAt));

    public Task<SurfaceFeedState> AcknowledgeAsync(
        long expectedRevision,
        string sessionScopeHash,
        long sequence,
        DateTimeOffset cursorExpiresAt,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current => SurfaceFeedTransitions.Acknowledge(
            current,
            expectedRevision,
            sessionScopeHash,
            sequence,
            cursorExpiresAt,
            now));

    public Task<SurfaceFeedState> RevokeSessionAsync(
        long expectedRevision,
        string sessionScopeHash,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current => SurfaceFeedTransitions.RevokeSession(
            current,
            expectedRevision,
            sessionScopeHash,
            now));

    public Task<SurfaceActionConsumption> ConsumeActionAsync(
        long expectedRevision,
        string bindingId,
        string tokenHash,
        string idempotencyKey,
        string operationId,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current =>
        {
            var result = SurfaceFeedTransitions.ConsumeAction(
                current,
                expectedRevision,
                bindingId,
                tokenHash,
                idempotencyKey,
                operationId,
                now);
            return (result.State, result);
        });

    public Task<SurfaceFeedState> RenewActionBindingsAsync(long expectedRevision, DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current =>
            SurfaceFeedTransitions.RenewActionBindings(current, expectedRevision, now));

    public Task<SurfaceFeedState> RebuildAsync(
        long expectedRevision,
        string projectionId,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current =>
            SurfaceFeedTransitions.Rebuild(current, expectedRevision, projectionId, now));
}
