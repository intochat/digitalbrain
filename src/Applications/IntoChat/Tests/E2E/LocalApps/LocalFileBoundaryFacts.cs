using IntoChat.LocalFiles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace IntoChat.Tests.E2E.LocalApps;

public sealed class LocalFileBoundaryFacts
{
    [Fact]
    public async Task LargeFoldersArePagedAndOversizedSourcesAreRejectedBeforeReading()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-page-").FullName;
        try
        {
            for (var i = 0; i < 105; i++) { await File.WriteAllTextAsync(Path.Combine(root, $"file-{i:000}.txt"), "fixture", ct); }
            using (var huge = File.Create(Path.Combine(root, "huge.png"))) { huge.SetLength(LocalFilesOptions.MaxSourceBytes + 1); }
            var files = new LocalFileStore(Options.Create(new LocalFilesOptions { Roots = new() { ["downloads"] = root }, AssetDirectory = Path.Combine(root, "assets") }), new EphemeralDataProtectionProvider());
            var first = await files.ListAsync("scope", null, 0, "name", "file-", ct);
            Assert.Equal(100, first.Items.Count);
            Assert.Equal(100, first.NextOffset);
            var next = await files.ListAsync("scope", first.FolderId, 100, "name", "file-", ct);
            Assert.Equal(5, next.Items.Count);
            Assert.Null(next.NextOffset);
            var oversized = Assert.Single((await files.ListAsync("scope", null, 0, "name", "huge", ct)).Items);
            await Assert.ThrowsAsync<ArgumentException>(() => files.SnapshotImageAsync("scope", oversized.Id, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LinkedFoldersDoNotAppearInTheAllowedRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-links-").FullName;
        var outside = Directory.CreateTempSubdirectory("intochat-outside-").FullName;
        try
        {
            try { Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside); }
            catch (UnauthorizedAccessException) { Assert.Skip("The OS does not allow creating the symbolic-link fixture."); }
            catch (IOException) { Assert.Skip("The OS does not allow creating the symbolic-link fixture."); }
            var files = new LocalFileStore(Options.Create(new LocalFilesOptions { Roots = new() { ["downloads"] = root } }), new EphemeralDataProtectionProvider());
            Assert.Empty((await files.ListAsync("scope", null, 0, "name", "", ct)).Items);
        }
        finally
        {
            if (Directory.Exists(Path.Combine(root, "linked"))) { Directory.Delete(Path.Combine(root, "linked")); }
            Directory.Delete(root, true); Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task HandlesAreScopedAndSnapshotsPreserveOriginalBytes()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-files-").FullName;
        try
        {
            var downloads = Directory.CreateDirectory(Path.Combine(root, "Downloads")).FullName;
            var source = Path.Combine(downloads, "Untitled.png");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==");
            await File.WriteAllBytesAsync(source, png, ct);
            var files = new LocalFileStore(Options.Create(new LocalFilesOptions { Roots = new() { ["downloads"] = downloads }, AssetDirectory = Path.Combine(root, "assets") }), new EphemeralDataProtectionProvider());
            var page = await files.ListAsync("workspace-a", null, 0, "name", "", ct);
            var item = Assert.Single(page.Items);
            Assert.Equal("Untitled.png", item.Label);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.SnapshotImageAsync("workspace-b", item.Id, ct));
            var asset = await files.SnapshotImageAsync("workspace-a", item.Id, ct);
            await using var stream = files.OpenAsset("workspace-a", asset.Id);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            Assert.Equal(png, copy.ToArray());
            Assert.Equal(png, await File.ReadAllBytesAsync(source, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.ListAsync("workspace-a", "../outside", 0, "name", "", ct));
            await File.WriteAllBytesAsync(source, [1, 2, 3], ct);
            await Assert.ThrowsAsync<IOException>(() => files.SnapshotImageAsync("workspace-a", item.Id, ct));
            File.Delete(source);
            await Assert.ThrowsAsync<FileNotFoundException>(() => files.SnapshotImageAsync("workspace-a", item.Id, ct));
        }
        finally { Directory.Delete(root, true); }
    }
}
