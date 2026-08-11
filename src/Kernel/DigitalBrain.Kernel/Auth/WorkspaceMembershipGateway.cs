using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Kernel;

internal interface IWorkspaceMembershipGateway
{
    Task AddMemberAsync(
        ActorContext actor,
        PrincipalId principalId,
        string username,
        WorkspaceRole role,
        CancellationToken cancellationToken);

    Task<Membership> ReadMembershipAsync(ActorContext actor, CancellationToken cancellationToken);
}

internal sealed class WorkspaceMembershipGateway(IDigitalBrain brain) : IWorkspaceMembershipGateway
{
    public Task AddMemberAsync(
        ActorContext actor,
        PrincipalId principalId,
        string username,
        WorkspaceRole role,
        CancellationToken cancellationToken)
        => brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(actor, principalId, username, role),
            cancellationToken);

    public Task<Membership> ReadMembershipAsync(ActorContext actor, CancellationToken cancellationToken)
        => brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(actor),
            cancellationToken);
}
