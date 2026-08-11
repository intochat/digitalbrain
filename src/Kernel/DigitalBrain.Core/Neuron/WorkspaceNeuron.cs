using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.workspace-state")]
internal sealed record WorkspaceState(
    [property: Id(0)] string Name,
    [property: Id(1)] IReadOnlyList<WorkspaceMember> Members);

[GrainType(IWorkspace.GrainTypeName)]
internal sealed class WorkspaceNeuron : Neuron, IWorkspace
{
    private const string StateName = "workspace.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<WorkspaceState> _states;

    public WorkspaceNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<WorkspaceState>>();
    }

    public Task HandleAsync(AddMember synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireActor(synapse.Actor);
        RequireUsername(synapse.Username);

        var state = Load();
        var members = state.Members.ToList();

        if (members.Count == 0)
        {
            RequireBootstrap(synapse);
        }
        else
        {
            RequireMutator(synapse.Actor, members);
        }

        if (members.Any(member => member.PrincipalId == synapse.PrincipalId))
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' already has member '{synapse.PrincipalId}'.");
        }

        var member = new WorkspaceMember(synapse.PrincipalId, synapse.Username.Trim(), synapse.Role);
        members.Add(member);
        Stage(state with { Members = members });

        return ReplyAsync(
            new MemberAdded(synapse.Actor, TimeProvider.GetUtcNow(), member),
            cancellationToken);
    }

    public Task HandleAsync(ChangeRole synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireActor(synapse.Actor);

        var state = Load();
        var members = state.Members.ToList();
        RequireMutator(synapse.Actor, members);

        var index = members.FindIndex(member => member.PrincipalId == synapse.PrincipalId);
        if (index < 0)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' has no member '{synapse.PrincipalId}' whose role can change.");
        }

        var current = members[index];
        if (current.Role == synapse.Role)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' member '{synapse.PrincipalId}' already has role '{synapse.Role}'.");
        }

        if (current.Role == WorkspaceRole.Owner
            && synapse.Role != WorkspaceRole.Owner
            && OwnerCount(members) == 1)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' refuses to demote the last Owner.");
        }

        members[index] = current with { Role = synapse.Role };
        Stage(state with { Members = members });

        return ReplyAsync(
            new RoleChanged(
                synapse.Actor,
                TimeProvider.GetUtcNow(),
                synapse.PrincipalId,
                current.Role,
                synapse.Role),
            cancellationToken);
    }

    public Task HandleAsync(RemoveMember synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireActor(synapse.Actor);

        var state = Load();
        var members = state.Members.ToList();
        RequireMutator(synapse.Actor, members);

        var index = members.FindIndex(member => member.PrincipalId == synapse.PrincipalId);
        if (index < 0)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' has no member '{synapse.PrincipalId}' to remove.");
        }

        var removed = members[index];
        if (removed.Role == WorkspaceRole.Owner && OwnerCount(members) == 1)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' refuses to remove the last Owner.");
        }

        members.RemoveAt(index);
        Stage(state with { Members = members });

        return ReplyAsync(
            new MemberRemoved(synapse.Actor, TimeProvider.GetUtcNow(), removed),
            cancellationToken);
    }

    public Task HandleAsync(ReadMembership synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireActor(synapse.Actor);

        var state = Load();
        if (!state.Members.Any(member => member.PrincipalId == synapse.Actor.PrincipalId))
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' refuses membership reads by non-members.");
        }

        return ReplyAsync(new Membership(state.Name, state.Members), cancellationToken);
    }

    private WorkspaceState Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : new WorkspaceState(Id.Name, []);

    private void Stage(WorkspaceState state) => _state.Value = _states.SerializeToArray(state);

    private void RequireBootstrap(AddMember synapse)
    {
        if (synapse.Role != WorkspaceRole.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' bootstrap requires the first member to be an Owner.");
        }

        if (synapse.Actor.PrincipalId != synapse.PrincipalId)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' bootstrap requires the actor to be the first Owner.");
        }
    }

    private void RequireMutator(ActorContext actor, IReadOnlyList<WorkspaceMember> members)
    {
        var actorMember = members.FirstOrDefault(member => member.PrincipalId == actor.PrincipalId);
        if (actorMember is null)
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' refuses membership mutations by non-members.");
        }

        if (actorMember.Role is not (WorkspaceRole.Owner or WorkspaceRole.Admin))
        {
            throw new NeuronAuthorizationException(
                $"Workspace '{Id}' refuses membership mutations by role '{actorMember.Role}'.");
        }
    }

    private static int OwnerCount(IEnumerable<WorkspaceMember> members)
        => members.Count(member => member.Role == WorkspaceRole.Owner);

    private static void RequireActor(ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(actor.Username))
        {
            throw new NeuronAuthorizationException("An actor requires a non-empty username.");
        }
    }

    private static void RequireUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new NeuronAuthorizationException("A workspace member requires a non-empty username.");
        }
    }
}
