namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace.add-member")]
public sealed record AddMember(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] PrincipalId PrincipalId,
    [property: Id(2)] string Username,
    [property: Id(3)] WorkspaceRole Role) : RequestSynapse<MemberAdded>;

[GenerateSerializer]
[Alias("db.workspace.member-added")]
public sealed record MemberAdded(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] DateTimeOffset At,
    [property: Id(2)] WorkspaceMember Member) : Synapse;

[GenerateSerializer]
[Alias("db.workspace.change-role")]
public sealed record ChangeRole(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] PrincipalId PrincipalId,
    [property: Id(2)] WorkspaceRole Role) : RequestSynapse<RoleChanged>;

[GenerateSerializer]
[Alias("db.workspace.role-changed")]
public sealed record RoleChanged(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] DateTimeOffset At,
    [property: Id(2)] PrincipalId PrincipalId,
    [property: Id(3)] WorkspaceRole PreviousRole,
    [property: Id(4)] WorkspaceRole Role) : Synapse;

[GenerateSerializer]
[Alias("db.workspace.remove-member")]
public sealed record RemoveMember(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] PrincipalId PrincipalId) : RequestSynapse<MemberRemoved>;

[GenerateSerializer]
[Alias("db.workspace.member-removed")]
public sealed record MemberRemoved(
    [property: Id(0)] ActorContext Actor,
    [property: Id(1)] DateTimeOffset At,
    [property: Id(2)] WorkspaceMember Member) : Synapse;

[GenerateSerializer]
[Alias("db.workspace.read-membership")]
public sealed record ReadMembership(
    [property: Id(0)] ActorContext Actor) : RequestSynapse<Membership>;

[GenerateSerializer]
[Alias("db.workspace.membership")]
public sealed record Membership(
    [property: Id(0)] string Name,
    [property: Id(1)] IReadOnlyList<WorkspaceMember> Members) : Synapse;
