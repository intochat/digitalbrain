using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Packages;

public sealed class PackageLineageFacts
{
    private static readonly PackageId Upstream = PackageId.Parse("alice/researcher");
    private static readonly PackageId Fork = PackageId.Parse("bob/researcher");

    [Fact]
    public async Task AForkCarriesTheWholeLineageAndRemembersItsOrigin()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Commit(brain, "alice", Upstream, "Research");
        var second = await Commit(brain, "alice", Upstream, "Investigate");

        Caller.As("bob");
        var forked = await brain.Get<IPackage>(Fork.ToString()).Fork(new(Guid.NewGuid(), new(Upstream, second.Id)));

        Assert.Equal(second.Id, forked.Head);
        Assert.Equal(new PackageRevisionRef(Upstream, second.Id), forked.ForkedFrom);
        Assert.Equal([first.Id, second.Id], forked.History.Select(item => item.Id));
        Assert.Equal("alice", (await brain.Get<IPackage>(Fork.ToString()).ReadRevision(first.Id)).Author);
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IPackage>(Fork.ToString()).Fork(new(Guid.NewGuid(), new(Upstream, first.Id))));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => brain.Get<IPackage>("carol/researcher").Fork(new(Guid.NewGuid(), new(Upstream, first.Id))));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => brain.Get<IPackage>("bob/other").Fork(new(Guid.NewGuid(), new(Upstream, "missing"))));
    }

    [Fact]
    public async Task PullFastForwardsUntilTheForkDivergesThenAMergeCommitReconcilesBoth()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Commit(brain, "alice", Upstream, "Research");
        await ForkFrom(brain, first);
        var second = await Commit(brain, "alice", Upstream, "Investigate");

        Caller.As("bob");
        var pulled = await brain.Get<IPackage>(Fork.ToString()).Pull(new(Guid.NewGuid(), new(Upstream, second.Id)));
        Assert.Equal(second.Id, pulled.Head);

        var local = await Commit(brain, "bob", Fork, "Summarize");
        var third = await Commit(brain, "alice", Upstream, "Explore");
        Caller.As("bob");
        var diverged = await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IPackage>(Fork.ToString()).Pull(new(Guid.NewGuid(), new(Upstream, third.Id))));
        Assert.Contains("merge", diverged.Message, StringComparison.OrdinalIgnoreCase);

        var merged = await brain.Get<IPackage>(Fork.ToString()).Commit(
            brain.Commit(local.Id, PackageSamples.Researcher("Explore and summarize"), "Merge alice", new(Upstream, third.Id)));

        Assert.Equal([local.Id, third.Id], merged.Parents);
        Assert.Contains(third.Id, (await brain.Get<IPackage>(Fork.ToString()).Read()).History.Select(item => item.Id));
        var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IPackage>(Fork.ToString()).Commit(
            brain.Commit(merged.Id, PackageSamples.Researcher("Again"), "Merge again", new(Upstream, third.Id))));
        Assert.Contains("already", repeated.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnAcceptedProposalFastForwardsUpstreamAndKeepsTheContributorsAuthorship()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Commit(brain, "alice", Upstream, "Research");
        await ForkFrom(brain, first);
        var draft = await Commit(brain, "bob", Fork, "Summarize");
        var polished = await Commit(brain, "bob", Fork, "Summarize briefly");

        Caller.As("bob");
        var proposal = await brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, polished.Id), "Summaries"));
        Assert.Equal(1, proposal.Number);
        Assert.Equal(ProposalStatus.Open, proposal.Status);
        Assert.Equal("bob", proposal.Author);

        Caller.As("alice");
        var accepted = await brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), proposal.Number));

        Assert.Equal(polished.Id, accepted.Head);
        Assert.Equal([first.Id, draft.Id, polished.Id], accepted.History.Select(item => item.Id));
        Assert.Equal("bob", accepted.History[^1].Author);
        Assert.Equal(ProposalStatus.Accepted, Assert.Single(accepted.Proposals).Status);
    }

    [Fact]
    public async Task AProposalBehindUpstreamIsRefusedUntilTheContributorMerges()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Commit(brain, "alice", Upstream, "Research");
        await ForkFrom(brain, first);
        var local = await Commit(brain, "bob", Fork, "Summarize");
        var upstream = await Commit(brain, "alice", Upstream, "Investigate");

        Caller.As("bob");
        var proposal = await brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, local.Id), "Summaries"));
        Caller.As("alice");
        var behind = await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), proposal.Number)));
        Assert.Contains("merge", behind.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(upstream.Id, (await brain.Get<IPackage>(Upstream.ToString()).Read()).Head);

        Caller.As("bob");
        var merged = await brain.Get<IPackage>(Fork.ToString()).Commit(
            brain.Commit(local.Id, PackageSamples.Researcher("Investigate and summarize"), "Merge alice", new(Upstream, upstream.Id)));
        var updated = await brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, merged.Id), "Summaries"));
        Assert.Equal(proposal.Number, updated.Number);

        Caller.As("alice");
        var accepted = await brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), proposal.Number));
        Assert.Equal(merged.Id, accepted.Head);
    }

    [Fact]
    public async Task OnlyTheContributorProposesAndOnlyTheOwnerAccepts()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Commit(brain, "alice", Upstream, "Research");
        await ForkFrom(brain, first);
        var local = await Commit(brain, "bob", Fork, "Summarize");

        Caller.As("carol");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, local.Id), "Not mine")));
        Caller.As("alice");
        await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Upstream, first.Id), "Self")));

        Caller.As("bob");
        var proposal = await brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, local.Id), "Summaries"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), proposal.Number)));
        Caller.As("alice");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), 42)));
    }

    [Fact]
    public async Task TheOwnerOrTheAuthorClosesAProposalAndAFreshOneCanBeOpened()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Commit(brain, "alice", Upstream, "Research");
        await ForkFrom(brain, first);
        var local = await Commit(brain, "bob", Fork, "Summarize");
        Caller.As("bob");
        var proposal = await brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, local.Id), "Summaries"));

        Caller.As("carol");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => brain.Get<IPackage>(Upstream.ToString()).Close(new(Guid.NewGuid(), proposal.Number)));
        Caller.As("alice");
        var closed = await brain.Get<IPackage>(Upstream.ToString()).Close(new(Guid.NewGuid(), proposal.Number));
        Assert.Equal(ProposalStatus.Closed, Assert.Single(closed.Proposals).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), proposal.Number)));

        Caller.As("bob");
        var reopened = await brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, local.Id), "Summaries, again"));
        Assert.Equal(2, reopened.Number);
        var withdrawn = await brain.Get<IPackage>(Upstream.ToString()).Close(new(Guid.NewGuid(), reopened.Number));
        Assert.All(withdrawn.Proposals, item => Assert.Equal(ProposalStatus.Closed, item.Status));
    }

    [Fact]
    public async Task ProposalsNeedAnExistingUpstreamAndAcceptingTwiceIsHarmless()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Commit(brain, "alice", Upstream, "Research");
        await ForkFrom(brain, first);
        var local = await Commit(brain, "bob", Fork, "Summarize");

        Caller.As("bob");
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IPackage>("alice/empty").Propose(new(Guid.NewGuid(), new(Fork, local.Id), "Nothing to change")));
        var proposal = await brain.Get<IPackage>(Upstream.ToString()).Propose(new(Guid.NewGuid(), new(Fork, local.Id), "Summaries"));
        Caller.As("alice");
        await brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), proposal.Number));

        var again = await brain.Get<IPackage>(Upstream.ToString()).Accept(new(Guid.NewGuid(), proposal.Number));

        Assert.Equal(local.Id, again.Head);
    }

    private static async Task<PackageRevision> Commit(PackageBrain brain, string principal, PackageId package, string verb)
    {
        Caller.As(principal);
        var target = brain.Get<IPackage>(package.ToString());
        var head = (await target.Read()).Head;
        return await target.Commit(brain.Commit(head, PackageSamples.Researcher(verb), verb));
    }

    private static async Task ForkFrom(PackageBrain brain, PackageRevision revision)
    {
        Caller.As("bob");
        await brain.Get<IPackage>(Fork.ToString()).Fork(new(Guid.NewGuid(), new(Upstream, revision.Id)));
    }
}
