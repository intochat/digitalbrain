using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.runtime.session.v1")]
internal sealed class SessionNeuron(
    [PersistentState("session", RuntimeStateStorageProviders.Sessions)]
    IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector) : Grain, ISessionNeuron
{
    private EncryptedPersistentState<SessionState>? _state;

    private EncryptedPersistentState<SessionState> State => _state ??= new(
        persistentState,
        protector,
        this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Session grains require a string key."),
        RuntimeStateKinds.Session,
        RuntimeStateSchemas.Session,
        SessionState.Empty,
        static value => value.Revision,
        SessionTransitions.Validate);

    public Task<SessionState> ReadAsync() => State.ReadAsync();

    public Task<SessionState> InitializeAsync(
        long expectedRevision,
        string opaqueSessionId,
        string audience,
        SessionIdentity identity,
        AuthAssurance assurance,
        string[] grants,
        string refreshTokenHash,
        DateTimeOffset refreshExpiresAt) =>
        State.UpdateAsync(expectedRevision, current => SessionTransitions.Initialize(current, expectedRevision, opaqueSessionId, audience, identity, assurance, grants, refreshTokenHash, refreshExpiresAt));

    public Task<SessionRotation> RotateRefreshAsync(long expectedRevision, string presentedRefreshHash, string replacementRefreshHash, DateTimeOffset replacementExpiresAt, DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current =>
        {
            var result = SessionTransitions.RotateRefresh(current, expectedRevision, presentedRefreshHash, replacementRefreshHash, replacementExpiresAt, now);
            return (result.State, result);
        });

    public Task<SessionState> RevokeAsync(long expectedRevision, DateTimeOffset revokedAt) =>
        State.UpdateAsync(expectedRevision, current =>
            SessionTransitions.Revoke(current, expectedRevision, revokedAt));

    public async Task<bool> IsAccessValidAsync(long sessionVersion, DateTimeOffset now) =>
        SessionTransitions.IsAccessValid(await State.ReadAsync(), sessionVersion, now);
}
