using DigitalBrain.Testing.Tests.Unit.Apps;
using DigitalBrain.UI;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// 04-09 Task 2: presentation assertions over the DigitalBrain.UI value tree, without a renderer.
public sealed class PresentationAssertionFacts
{
    [Fact]
    public async Task After_open_the_side_entries_and_their_trailing_text_are_readable()
    {
        await using var os = OS.Local().With<IStorageLike>("c", new StorageLikeFake("c", used: 10, total: 100));
        var files = await os.Install<FilesLike>();

        Assert.False(files.Side.IsSet);
        await files.Request("open");

        var entry = Assert.Single(files.Side.Entries);
        Assert.Equal("c", entry.Id);
        Assert.Equal("10 GB of 100 GB", entry.Trailing);
        Assert.Equal("Storages", files.Side.Title);
        Assert.Equal(["content"], files.Main.Texts);
    }

    [Fact]
    public async Task Find_returns_the_node_or_throws_listing_the_ids_there_are()
    {
        await using var os = OS.Local().With<IStorageLike>("c", new StorageLikeFake("c"));
        var files = await os.Install<FilesLike>();
        await files.Request("open");

        Assert.Equal("Filter names", Assert.IsType<TextField>(files.Main.Find("filter")).Placeholder);
        Assert.Equal("quota unknown", files.Side.Entry("c").Trailing);
        Assert.IsType<Entry>(files.Find("c"));

        var missing = Assert.Throws<PresentationAssertionException>(() => files.Side.Find("drive"));
        Assert.Contains("Ids: c", missing.Message, StringComparison.Ordinal);
        var nowhere = Assert.Throws<PresentationAssertionException>(() => files.Find("drive"));
        Assert.Contains("Main ids: filter. Side ids: c.", nowhere.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_presentation_published_in_a_reaction_is_visible_after_Idle_and_the_latest_wins()
    {
        await using var os = OS.Local().With<IStorageLike>("c", new StorageLikeFake("c"));
        var files = await os.Install<FilesLike>();
        var tally = await os.Install<TallyApp>();

        _ = files.Request("open");
        _ = files.Request("search", "report");
        _ = tally.Request("add");
        _ = tally.Request("add");
        await os.Idle();

        Assert.Equal("Results", files.Main.Title);
        Assert.Equal(["results for 'report'"], files.Main.Texts);
        Assert.Equal("Storages", files.Side.Title);
        Assert.Equal(["searching 'report'"], files.Toasts);
        Assert.Equal("2", tally.Side.Entry("count").Trailing);
        Assert.Equal(["count is 2"], tally.Main.Texts);
        Assert.Null(tally.PresentationError);
    }

    // DESIGN section 6 as written, apart from the capability helper (With<IStorageLike> for WithStorage) and plain asserts for FluentAssertions.
    [Fact]
    public async Task Lists_every_storage_with_quota()
    {
        await using var os = OS.Local()
            .With<IStorageLike>("drive", new StorageLikeFake("drive"))
            .With<IStorageLike>("c", new StorageLikeFake("c", used: 10, total: 100));
        var files = await os.Install<FilesLike>();

        await files.Request("open");

        Assert.Equal(2, files.Side.Entries.Count);
        Assert.Contains(files.Side.Entries, e => e.Id == "c" && e.Trailing == "10 GB of 100 GB");
        Assert.Contains(files.Side.Entries, e => e.Id == "drive" && e.Trailing == "quota unknown");

        // The Phase 8 generated baseline: every operation with empty input, no presentation error, side and main set.
        foreach (var operation in files.Operations)
        {
            await files.Request(operation);
        }
        Assert.Null(files.PresentationError);
        Assert.True(files.Side.IsSet);
        Assert.True(files.Main.IsSet);
    }
}
