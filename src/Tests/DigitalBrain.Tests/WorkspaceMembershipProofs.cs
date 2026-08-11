using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class WorkspaceMembershipProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task EmptyWorkspaceAcceptsTheFirstOwnerAsBootstrap()
    {
        var brain = fixture.BrainFor("ws-bootstrap");
        var owner = Principal("alice");

        var added = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, owner.PrincipalId, owner.Username, WorkspaceRole.Owner),
            TestContext.Current.CancellationToken);

        Assert.Equal(owner.PrincipalId, added.Member.PrincipalId);
        Assert.Equal(owner.Username, added.Member.Username);
        Assert.Equal(WorkspaceRole.Owner, added.Member.Role);
        Assert.Equal(owner, added.Actor);

        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);

        Assert.Equal(IWorkspace.InstanceName, membership.Name);
        var member = Assert.Single(membership.Members);
        Assert.Equal(owner.PrincipalId, member.PrincipalId);
        Assert.Equal(WorkspaceRole.Owner, member.Role);
    }

    [Fact]
    public async Task OwnerCanAddAdminBuilderAndViewer()
    {
        var brain = fixture.BrainFor("ws-add-roles");
        var owner = Principal("owner");
        await BootstrapOwnerAsync(brain, owner);

        var admin = Principal("admin");
        var builder = Principal("builder");
        var viewer = Principal("viewer");

        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, admin.PrincipalId, admin.Username, WorkspaceRole.Admin),
            TestContext.Current.CancellationToken);
        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, builder.PrincipalId, builder.Username, WorkspaceRole.Builder),
            TestContext.Current.CancellationToken);
        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, viewer.PrincipalId, viewer.Username, WorkspaceRole.Viewer),
            TestContext.Current.CancellationToken);

        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);

        Assert.Equal(4, membership.Members.Count);
        Assert.Contains(membership.Members, member => member.PrincipalId == admin.PrincipalId && member.Role == WorkspaceRole.Admin);
        Assert.Contains(membership.Members, member => member.PrincipalId == builder.PrincipalId && member.Role == WorkspaceRole.Builder);
        Assert.Contains(membership.Members, member => member.PrincipalId == viewer.PrincipalId && member.Role == WorkspaceRole.Viewer);
    }

    [Fact]
    public async Task AdminCanMutateMembership()
    {
        var brain = fixture.BrainFor("ws-admin-mutate");
        var owner = Principal("owner");
        var admin = Principal("admin");
        var recruit = Principal("recruit");
        await BootstrapOwnerAsync(brain, owner);
        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, admin.PrincipalId, admin.Username, WorkspaceRole.Admin),
            TestContext.Current.CancellationToken);

        var added = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(admin, recruit.PrincipalId, recruit.Username, WorkspaceRole.Viewer),
            TestContext.Current.CancellationToken);

        Assert.Equal(admin, added.Actor);
        Assert.Equal(WorkspaceRole.Viewer, added.Member.Role);
    }

    [Fact]
    public async Task BuilderCannotMutateMembership()
    {
        var brain = fixture.BrainFor("ws-builder-refuse");
        var workspace = IWorkspace.ForOwner(brain.Owner);
        var owner = Principal("owner");
        var builder = Principal("builder");
        var recruit = Principal("recruit");
        await BootstrapOwnerAsync(brain, owner);
        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, builder.PrincipalId, builder.Username, WorkspaceRole.Builder),
            TestContext.Current.CancellationToken);

        await brain.FireAsync(
            workspace,
            new AddMember(builder, recruit.PrincipalId, recruit.Username, WorkspaceRole.Viewer),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is AddMember { Username: "recruit" });

        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(membership.Members, member => member.PrincipalId == recruit.PrincipalId);
    }

    [Fact]
    public async Task NonMemberMutationsAreRefused()
    {
        var brain = fixture.BrainFor("ws-stranger-refuse");
        var workspace = IWorkspace.ForOwner(brain.Owner);
        var owner = Principal("owner");
        var stranger = Principal("stranger");
        var recruit = Principal("recruit");
        await BootstrapOwnerAsync(brain, owner);

        await brain.FireAsync(
            workspace,
            new AddMember(stranger, recruit.PrincipalId, recruit.Username, WorkspaceRole.Viewer),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is AddMember { Username: "recruit" });

        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(membership.Members, member => member.PrincipalId == recruit.PrincipalId);
    }

    [Fact]
    public async Task LastOwnerCannotBeRemovedOrDemoted()
    {
        var brain = fixture.BrainFor("ws-last-owner");
        var workspace = IWorkspace.ForOwner(brain.Owner);
        var owner = Principal("owner");
        await BootstrapOwnerAsync(brain, owner);

        await brain.FireAsync(
            workspace,
            new RemoveMember(owner, owner.PrincipalId),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is RemoveMember remove
                && remove.PrincipalId == owner.PrincipalId);

        await brain.FireAsync(
            workspace,
            new ChangeRole(owner, owner.PrincipalId, WorkspaceRole.Admin),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is ChangeRole change
                && change.PrincipalId == owner.PrincipalId
                && change.Role == WorkspaceRole.Admin);

        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);
        var sole = Assert.Single(membership.Members);
        Assert.Equal(owner.PrincipalId, sole.PrincipalId);
        Assert.Equal(WorkspaceRole.Owner, sole.Role);
    }

    [Fact]
    public async Task SecondOwnerCanBeRemovedAndFirstOwnerCanDemoteWhenAnotherOwnerRemains()
    {
        var brain = fixture.BrainFor("ws-two-owners");
        var owner = Principal("owner");
        var coOwner = Principal("co-owner");
        await BootstrapOwnerAsync(brain, owner);
        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, coOwner.PrincipalId, coOwner.Username, WorkspaceRole.Owner),
            TestContext.Current.CancellationToken);

        var demoted = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ChangeRole(owner, coOwner.PrincipalId, WorkspaceRole.Admin),
            TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceRole.Owner, demoted.PreviousRole);
        Assert.Equal(WorkspaceRole.Admin, demoted.Role);

        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ChangeRole(owner, demoted.PrincipalId, WorkspaceRole.Owner),
            TestContext.Current.CancellationToken);

        var removed = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new RemoveMember(owner, coOwner.PrincipalId),
            TestContext.Current.CancellationToken);
        Assert.Equal(coOwner.PrincipalId, removed.Member.PrincipalId);

        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);
        Assert.Single(membership.Members);
    }

    [Fact]
    public async Task MembershipMutationsJournalTheActingActor()
    {
        var brain = fixture.BrainFor("ws-audit");
        var workspace = IWorkspace.ForOwner(brain.Owner);
        var owner = Principal("owner");
        var recruit = Principal("recruit");
        await BootstrapOwnerAsync(brain, owner);

        var added = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, recruit.PrincipalId, recruit.Username, WorkspaceRole.Viewer),
            TestContext.Current.CancellationToken);

        Assert.Equal(owner, added.Actor);
        Assert.True(added.At > DateTimeOffset.MinValue);

        var incoming = await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is AddMember command
                && command.PrincipalId == recruit.PrincipalId
                && command.Actor == owner);
        Assert.Equal(owner, ((AddMember)incoming.Synapse).Actor);

        var outgoing = await Journals.WaitForAsync(
            brain, workspace, JournalKind.Outgoing,
            delivery => delivery.Synapse is MemberAdded fact
                && fact.Member.PrincipalId == recruit.PrincipalId);
        Assert.Equal(owner, ((MemberAdded)outgoing.Synapse).Actor);
        Assert.Equal(added.At, ((MemberAdded)outgoing.Synapse).At);

        var changed = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ChangeRole(owner, recruit.PrincipalId, WorkspaceRole.Builder),
            TestContext.Current.CancellationToken);
        Assert.Equal(owner, changed.Actor);

        var removed = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new RemoveMember(owner, recruit.PrincipalId),
            TestContext.Current.CancellationToken);
        Assert.Equal(owner, removed.Actor);
        Assert.Equal(WorkspaceRole.Builder, removed.Member.Role);

        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Outgoing,
            delivery => delivery.Synapse is MemberRemoved fact
                && fact.Member.PrincipalId == recruit.PrincipalId
                && fact.Actor == owner);
    }

    [Fact]
    public async Task NonMemberCannotReadMembership()
    {
        var brain = fixture.BrainFor("ws-read-refuse");
        var workspace = IWorkspace.ForOwner(brain.Owner);
        var owner = Principal("owner");
        var stranger = Principal("stranger");
        await BootstrapOwnerAsync(brain, owner);

        await brain.FireAsync(
            workspace,
            new ReadMembership(stranger),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is ReadMembership read
                && read.Actor.PrincipalId == stranger.PrincipalId);

        var outgoing = await brain.ReadJournalAsync(
            workspace, JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Synapse is Membership);
    }

    [Fact]
    public async Task DuplicateMemberIsRefused()
    {
        var brain = fixture.BrainFor("ws-duplicate");
        var workspace = IWorkspace.ForOwner(brain.Owner);
        var owner = Principal("owner");
        var recruit = Principal("recruit");
        await BootstrapOwnerAsync(brain, owner);
        await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, recruit.PrincipalId, recruit.Username, WorkspaceRole.Viewer),
            TestContext.Current.CancellationToken);

        await brain.FireAsync(
            workspace,
            new AddMember(owner, recruit.PrincipalId, recruit.Username, WorkspaceRole.Builder),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is AddMember command
                && command.PrincipalId == recruit.PrincipalId
                && command.Role == WorkspaceRole.Builder);

        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);
        Assert.Single(membership.Members, member => member.PrincipalId == recruit.PrincipalId);
        Assert.Equal(
            WorkspaceRole.Viewer,
            membership.Members.Single(member => member.PrincipalId == recruit.PrincipalId).Role);
    }

    [Fact]
    public async Task EmptyWorkspaceRefusesNonOwnerBootstrap()
    {
        var brain = fixture.BrainFor("ws-bootstrap-role");
        var workspace = IWorkspace.ForOwner(brain.Owner);
        var actor = Principal("alice");

        await brain.FireAsync(
            workspace,
            new AddMember(actor, actor.PrincipalId, actor.Username, WorkspaceRole.Admin),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, workspace, JournalKind.Incoming,
            delivery => delivery.Synapse is AddMember { Role: WorkspaceRole.Admin });

        // No successful membership reply means still empty — confirmed via a fresh owner bootstrap path later.
        // Stranger read refuses; prove emptiness by bootstrapping for real and counting one.
        var owner = Principal("real-owner");
        await BootstrapOwnerAsync(brain, owner);
        var membership = await brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new ReadMembership(owner),
            TestContext.Current.CancellationToken);
        Assert.Single(membership.Members);
        Assert.Equal(owner.PrincipalId, membership.Members[0].PrincipalId);
    }

    private static Task BootstrapOwnerAsync(IDigitalBrain brain, ActorContext owner)
        => brain.Get<IWorkspace>(IWorkspace.InstanceName).FireAsync(
            new AddMember(owner, owner.PrincipalId, owner.Username, WorkspaceRole.Owner),
            TestContext.Current.CancellationToken);

    private static ActorContext Principal(string username)
        => new(PrincipalId.New(), username);
}
