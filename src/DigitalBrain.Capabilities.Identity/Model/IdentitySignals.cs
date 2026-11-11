namespace DigitalBrain.Capabilities.Identity.Model;

using DigitalBrain;

/// <summary>Domain fact emitted when a member is added to a brain.</summary>
/// <param name="Member">The member that was added.</param>
[Signal("identity.member-added")]
public sealed record MemberAdded(Member Member) : Signal;

/// <summary>Domain fact emitted when a member is removed from a brain.</summary>
/// <param name="BrainId">The brain identifier.</param>
/// <param name="PrincipalId">The principal identifier.</param>
/// <param name="AccountId">The account identifier.</param>
[Signal("identity.member-removed")]
public sealed record MemberRemoved(string BrainId, string PrincipalId, string AccountId) : Signal;

/// <summary>Domain fact emitted when a capability grant is recorded.</summary>
/// <param name="Grant">The recorded grant.</param>
[Signal("identity.grant-recorded")]
public sealed record GrantRecorded(Grant Grant) : Signal;

/// <summary>Domain fact emitted when a capability grant is revoked.</summary>
/// <param name="WorkspaceId">The workspace or brain identifier.</param>
/// <param name="AppId">The application identifier.</param>
/// <param name="SemanticTypeId">The semantic type identifier.</param>
/// <param name="Mode">The grant mode.</param>
[Signal("identity.grant-revoked")]
public sealed record GrantRevoked(string WorkspaceId, string AppId, string SemanticTypeId, GrantMode Mode) : Signal;

/// <summary>Domain fact emitted when a once-grant is consumed.</summary>
/// <param name="WorkspaceId">The workspace or brain identifier.</param>
/// <param name="AppId">The application identifier.</param>
/// <param name="SemanticTypeId">The semantic type identifier.</param>
[Signal("identity.grant-consumed")]
public sealed record GrantConsumed(string WorkspaceId, string AppId, string SemanticTypeId) : Signal;

/// <summary>Domain fact emitted when an account is ensured.</summary>
/// <param name="Account">The ensured account.</param>
[Signal("identity.account-ensured")]
public sealed record AccountEnsured(Account Account) : Signal;

/// <summary>Domain fact emitted when a brain is created.</summary>
/// <param name="BrainId">The brain identifier.</param>
/// <param name="AccountId">The owning account identifier.</param>
/// <param name="Kind">The brain kind.</param>
[Signal("identity.brain-created")]
public sealed record BrainCreated(string BrainId, string AccountId, BrainKind Kind) : Signal;

/// <summary>Domain fact emitted when a principal is registered.</summary>
/// <param name="PrincipalId">The registered principal identifier.</param>
/// <param name="DefaultMembership">The principal's default owner membership.</param>
[Signal("identity.principal-registered")]
public sealed record PrincipalRegistered(string PrincipalId, Member DefaultMembership) : Signal;

/// <summary>Domain fact emitted when an authenticated session starts.</summary>
/// <param name="TokenHash">The SHA256 hex hash of the session token.</param>
/// <param name="BrainId">The brain identifier.</param>
/// <param name="PrincipalId">The principal identifier.</param>
/// <param name="ExpiresAt">When the session expires.</param>
[Signal("identity.session-started")]
public sealed record SessionStarted(string TokenHash, string BrainId, string PrincipalId, DateTimeOffset ExpiresAt) : Signal;
