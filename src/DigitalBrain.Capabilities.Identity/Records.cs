namespace DigitalBrain.Capabilities.Identity;

/// <summary>A member's role in a brain.</summary>
public enum MemberRole
{
    /// <summary>The brain's owner.</summary>
    Owner = 0,

    /// <summary>A person the brain was shared with.</summary>
    Member = 1,

    /// <summary>An installed app's own principal; it acts only behind the app proxy.</summary>
    App = 2,
}

/// <summary>What kind of brain an id names.</summary>
public enum BrainKind
{
    /// <summary>A person's own brain; also the answer for an id nobody created.</summary>
    Personal = 0,

    /// <summary>A shared brain created for its creator's account.</summary>
    Circle = 1,
}

/// <summary>An account: the owner of brains.</summary>
public sealed record Account
{
    /// <summary>The account id.</summary>
    public required string AccountId { get; init; }

    /// <summary>The display name.</summary>
    public required string Name { get; init; }

    /// <summary>The principal that owns the account.</summary>
    public required string OwnerPrincipalId { get; init; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A principal's membership of one brain.</summary>
public sealed record Member
{
    /// <summary>The signed-in principal.</summary>
    public required string PrincipalId { get; init; }

    /// <summary>The account that owns the brain.</summary>
    public required string AccountId { get; init; }

    /// <summary>The brain.</summary>
    public required string BrainId { get; init; }

    /// <summary>The member's role.</summary>
    public required MemberRole Role { get; init; }

    /// <summary>The member's display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>When the principal joined.</summary>
    public DateTimeOffset JoinedAt { get; init; }
}

/// <summary>A brain a principal can reach.</summary>
/// <param name="Id">The brain id.</param>
/// <param name="Kind">Its kind.</param>
public sealed record BrainEntry(string Id, BrainKind Kind);

/// <summary>The outcome of provisioning: the member record and whether a brain was created by this call.</summary>
/// <param name="Member">The owner's (or registrant's) membership.</param>
/// <param name="NewBrain">True when this call created the brain; repeated calls answer false (idempotent).</param>
public sealed record BrainProvision(Member Member, bool NewBrain);

/// <summary>How long a grant lasts.</summary>
public enum GrantMode
{
    /// <summary>One call; the runtime spends it.</summary>
    Once = 0,

    /// <summary>Calls in one conversation.</summary>
    ThisChat = 1,

    /// <summary>Until revoked.</summary>
    Always = 2,
}

/// <summary>An owner's permission for an app to use one semantic type in a brain. Semantic type ids double as scopes.</summary>
public sealed record Grant
{
    /// <summary>The app granted.</summary>
    public required string AppId { get; init; }

    /// <summary>The semantic type, for example <c>person.birthDate</c>.</summary>
    public required string SemanticTypeId { get; init; }

    /// <summary>How long the grant lasts.</summary>
    public required GrantMode Mode { get; init; }

    /// <summary>The brain (workspace) the grant applies in.</summary>
    public required string WorkspaceId { get; init; }

    /// <summary>The conversation a <see cref="GrantMode.ThisChat"/> grant is bound to.</summary>
    public string? ConversationId { get; init; }

    /// <summary>When it was granted.</summary>
    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>True once revoked; a revoked grant covers nothing.</summary>
    public bool Revoked { get; init; }

    /// <summary>Optional specific neuron key the grant is scoped to (neuron-level Circle access).</summary>
    public string? TargetNeuronKey { get; init; }

    /// <summary>
    /// Whether this grant lets <paramref name="appId"/> use <paramref name="semanticTypeId"/> in
    /// <paramref name="workspaceId"/>, within <paramref name="conversationId"/> for a <see cref="GrantMode.ThisChat"/> grant.
    /// Ordinal comparison throughout.
    /// </summary>
    public bool Covers(string appId, string semanticTypeId, string workspaceId, string? conversationId) =>
        Covers(appId, semanticTypeId, workspaceId, conversationId, targetNeuronKey: null);

    /// <summary>
    /// Whether this grant lets <paramref name="appId"/> use <paramref name="semanticTypeId"/> on <paramref name="targetNeuronKey"/>
    /// in <paramref name="workspaceId"/>, within <paramref name="conversationId"/> for a <see cref="GrantMode.ThisChat"/> grant.
    /// </summary>
    public bool Covers(string appId, string semanticTypeId, string workspaceId, string? conversationId, string? targetNeuronKey) =>
        !Revoked
        && string.Equals(AppId, appId, StringComparison.Ordinal)
        && string.Equals(SemanticTypeId, semanticTypeId, StringComparison.Ordinal)
        && string.Equals(WorkspaceId, workspaceId, StringComparison.Ordinal)
        && (TargetNeuronKey is null || string.Equals(TargetNeuronKey, targetNeuronKey, StringComparison.Ordinal))
        && (Mode != GrantMode.ThisChat || (conversationId is not null && string.Equals(ConversationId, conversationId, StringComparison.Ordinal)));
}

/// <summary>Why a call was denied.</summary>
public enum CallDenial
{
    /// <summary>No authenticated caller.</summary>
    NotAuthenticated = 0,

    /// <summary>The caller was not stamped by a trusted edge.</summary>
    UntrustedCaller = 1,

    /// <summary>The target is outside the caller's brain.</summary>
    OutsideWorkspace = 2,

    /// <summary>No grant covers a requested semantic type.</summary>
    MissingGrant = 3,

    /// <summary>No allowance covers the call.</summary>
    MissingAllowance = 4,

    /// <summary>A limit was reached.</summary>
    LimitReached = 5,

    /// <summary>The app is disabled.</summary>
    AppDisabled = 6,
}

/// <summary>
/// A call to authorize. It carries no caller (D-15): <see cref="IGrants.Authorize"/> authorizes it for the caller the
/// runtime stamped on the current call.
/// </summary>
public sealed record CallRequest
{
    /// <summary>The neuron called.</summary>
    public required string TargetNeuron { get; init; }

    /// <summary>The operation called.</summary>
    public required string Operation { get; init; }

    /// <summary>The semantic types the call touches.</summary>
    public IReadOnlyList<string> SemanticTypeIds { get; init; } = [];

    /// <summary>The estimated compute cost.</summary>
    public decimal EstimatedCompute { get; init; }

    /// <summary>Whether the call changes state.</summary>
    public bool HasSideEffects { get; init; }

    /// <summary>Whether the target's own scope is enforced.</summary>
    public bool EnforceTarget { get; init; }

    /// <summary>The target scope, when enforced.</summary>
    public string? TargetScope { get; init; }

    /// <summary>Whether the operation is public.</summary>
    public bool PublicOperation { get; init; }
}

/// <summary>The answer to a <see cref="CallRequest"/>.</summary>
public sealed record CallDecision
{
    /// <summary>Whether the call may proceed.</summary>
    public bool Allowed { get; init; }

    /// <summary>Why it was denied.</summary>
    public CallDenial? Denial { get; init; }

    /// <summary>A human explanation.</summary>
    public string? Explanation { get; init; }

    /// <summary>An allowed decision that rests on a <see cref="GrantMode.Once"/> grant, which the runtime spends.</summary>
    public bool ConsumesOnce { get; init; }

    /// <summary>An allowed decision.</summary>
    public static CallDecision Allow() => new() { Allowed = true };

    /// <summary>A denied decision.</summary>
    public static CallDecision Deny(CallDenial denial, string explanation) => new() { Allowed = false, Denial = denial, Explanation = explanation };
}
