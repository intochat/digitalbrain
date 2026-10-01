using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Packages;

public sealed class PackageDirectoryFacts
{
    [Fact]
    public async Task PublishingListsTheStableRevisionUntilTheOwnerPublishesAnother()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/researcher");
        var directory = brain.Get<IPackageDirectory>(PackageDirectory.Key);
        var first = await package.Commit(brain.Commit(null, PackageSamples.Researcher("Research")));

        Assert.Empty(await directory.List());
        var published = await package.Publish(new(Guid.NewGuid(), first.Id));
        var second = await package.Commit(brain.Commit(first.Id, PackageSamples.Researcher("Investigate")));

        Assert.Equal(first.Id, published.Published);
        var listing = Assert.Single(await directory.List());
        Assert.Equal(PackageId.Parse("alice/researcher"), listing.Package);
        Assert.Equal(first.Id, listing.Revision);
        Assert.Equal("Internet researcher", listing.Title);
        Assert.Null(listing.ForkedFrom);

        await package.Publish(new(Guid.NewGuid(), second.Id));
        Assert.Equal(second.Id, Assert.Single(await directory.List()).Revision);
    }

    [Fact]
    public async Task APublishedForkIsListedWithItsOrigin()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var upstream = brain.Get<IPackage>("alice/researcher");
        var first = await upstream.Commit(brain.Commit(null, PackageSamples.Researcher("Research")));
        await upstream.Publish(new(Guid.NewGuid(), first.Id));

        Caller.As("bob");
        var fork = brain.Get<IPackage>("bob/researcher");
        await fork.Fork(new(Guid.NewGuid(), new(PackageId.Parse("alice/researcher"), first.Id)));
        var changed = await fork.Commit(brain.Commit(first.Id, PackageSamples.Researcher("Summarize")));
        await fork.Publish(new(Guid.NewGuid(), changed.Id));

        var listings = await brain.Get<IPackageDirectory>(PackageDirectory.Key).List();
        var listed = Assert.Single(listings, item => item.Package.Owner == "bob");
        Assert.Equal(new PackageRevisionRef(PackageId.Parse("alice/researcher"), first.Id), listed.ForkedFrom);
        Assert.Equal(2, listings.Count);
    }

    [Fact]
    public async Task OnlyTheOwnerPublishesARevisionThePackageHas()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/researcher");
        var first = await package.Commit(brain.Commit(null, PackageSamples.Researcher("Research")));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => package.Publish(new(Guid.NewGuid(), "missing")));
        Caller.As("bob");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => package.Publish(new(Guid.NewGuid(), first.Id)));
        await brain.Get<IPackageDirectory>(PackageDirectory.Key).Refresh(PackageId.Parse("alice/researcher"));
        Assert.Empty(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List());
    }
}
