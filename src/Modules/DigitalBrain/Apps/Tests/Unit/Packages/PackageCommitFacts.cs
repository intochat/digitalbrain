using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Packages;

public sealed class PackageCommitFacts
{
    [Fact]
    public async Task OwnerCommitsAVerifiedRevisionAndARetryReturnsTheSameRevision()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var content = PackageSamples.Researcher("Research");
        var commit = new CommitPackage(Guid.NewGuid(), null, content, brain.Artifacts.Seal(content), "First version");
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/researcher");

        var first = await package.Commit(commit);
        var retried = await package.Commit(commit);

        Assert.Equal(first.Id, retried.Id);
        Assert.Empty(first.Parents);
        Assert.Equal("alice", first.Author);
        var snapshot = await package.Read();
        Assert.Equal(first.Id, snapshot.Head);
        Assert.Equal(first.Id, Assert.Single(snapshot.History).Id);
        Assert.Equal(content.Source, (await package.ReadRevision(first.Id)).Content.Source);
    }

    [Fact]
    public async Task EachCommitRecordsTheHeadItWasBasedOn()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/researcher");
        var first = await package.Commit(brain.Commit(null, PackageSamples.Researcher("Research")));

        var second = await package.Commit(brain.Commit(first.Id, PackageSamples.Researcher("Investigate")));

        Assert.Equal([first.Id], second.Parents);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal([first.Id, second.Id], (await package.Read()).History.Select(item => item.Id));
    }

    [Fact]
    public async Task AnArtifactBuiltFromOtherSourceCannotVouchForARevision()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var foreign = brain.Artifacts.Seal(PackageSamples.Researcher("Something else"));
        var commit = new CommitPackage(Guid.NewGuid(), null, PackageSamples.Researcher("Research"), foreign, "Unverified");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IPackage>("alice/researcher").Commit(commit));

        Assert.Contains("artifact", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await brain.Get<IPackage>("alice/researcher").Read()).Head);
    }

    [Fact]
    public async Task ACommitBasedOnAStaleHeadIsRefused()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/researcher");
        await package.Commit(brain.Commit(null, PackageSamples.Researcher("Research")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => package.Commit(brain.Commit(null, PackageSamples.Researcher("Investigate"))));
    }

    [Fact]
    public async Task OnlyTheOwnerCanCommit()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var package = brain.Get<IPackage>("alice/researcher");
        var commit = brain.Commit(null, PackageSamples.Researcher("Research"));

        Caller.As("bob");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => package.Commit(commit));
        Caller.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => package.Commit(commit));
    }

    [Fact]
    public async Task ReusingAnOperationIdForDifferentContentIsRefused()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/researcher");
        var commit = brain.Commit(null, PackageSamples.Researcher("Research"));
        await package.Commit(commit);

        var changed = PackageSamples.Researcher("Investigate");
        await Assert.ThrowsAsync<InvalidOperationException>(() => package.Commit(commit with { Content = changed, Artifact = brain.Artifacts.Seal(changed) }));
    }

    [Fact]
    public async Task MalformedPackagesAreRefusedBeforeAnythingIsStored()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var valid = PackageSamples.Researcher("Research");

        await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IPackage>("Alice/Researcher").Read());
        await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IPackage>("alice").Read());
        foreach (var invalid in new[]
        {
            valid with { Manifest = valid.Manifest with { Title = " " } },
            valid with { Manifest = valid.Manifest with { Settings = [.. valid.Manifest.Settings, valid.Manifest.Settings[0]] } },
            valid with { Manifest = valid.Manifest with { Operations = [new PackageOperation("not valid", "Spaces are not allowed.")] } },
            valid with { Manifest = valid.Manifest with { Settings = [new PackageSetting("apiToken", "Credentials never ship in a package.", "")] } },
            valid with { Manifest = valid.Manifest with { Settings = [new PackageSetting("app", "Collides with Behavior__App.", "")] } },
            valid with { Source = "" },
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IPackage>("alice/researcher").Commit(brain.Commit(null, invalid)));
        }
        await Assert.ThrowsAsync<KeyNotFoundException>(() => brain.Get<IPackage>("alice/researcher").ReadRevision("missing"));
    }
}
