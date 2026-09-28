using DigitalBrain.Testing.Unit;
using IntoChat.Apps;
using IntoChat.LocalFiles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
namespace IntoChat.Tests.Unit.LocalApps;

public sealed class ImageSaveFacts
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==");

    [Fact]
    public async Task SaveSurvivesSourceRemovalAndFreshServiceAndRetriesWithoutDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var root = Directory.CreateTempSubdirectory("intochat-save-").FullName;
        try
        {
            var source = Path.Combine(root, "Untitled.png");
            await File.WriteAllBytesAsync(source, Png, ct);
            var blobs = new MemoryAssetBlobStore();
            var options = Options.Create(new LocalFilesOptions { Roots = new() { ["downloads"] = root }, AssetDirectory = Path.Combine(root, "legacy-assets") });
            var files = new LocalFileStore(options, new EphemeralDataProtectionProvider(), blobs, brain.Grains);
            var listing = await files.ListHostAsync("scope", null, 0, "name", "Untitled.png", ct);
            var asset = await files.SnapshotImageAsync("scope", Assert.Single(listing.Items).Id, ct);
            File.Delete(source);
            var ticket = new SaveTicket(Guid.NewGuid().ToString(), 0, new(), asset);
            blobs.FailNextUpload = true;
            await Assert.ThrowsAsync<IOException>(() => new ImageSaveCoordinator(files, brain.Grains).Save("scope", ticket, new MemoryStream(Png), ct));
            var first = await new ImageSaveCoordinator(files, brain.Grains).Save("scope", ticket, new MemoryStream(Png), ct);
            await brain.DeactivateAsync(brain.Get<IWorkspaceAssets>("scope"), ct);
            await brain.DeactivateAsync(brain.Get<IImageSaveOperation>(first.EntryId[6..]), ct);
            var fresh = new LocalFileStore(Options.Create(new LocalFilesOptions { AssetDirectory = Path.Combine(root, "empty") }), new EphemeralDataProtectionProvider(), blobs, brain.Grains);
            var second = await new ImageSaveCoordinator(fresh, brain.Grains).Save("scope", ticket, new MemoryStream(Png), ct);
            Assert.Equal(first, second);
            var durable = await fresh.ListAsync("scope", null, 0, "name", "", ct);
            Assert.Equal(2, durable.Items.Count);
            var saved = await fresh.SnapshotImageAsync("scope", first.EntryId, ct);
            await using var opened = await fresh.OpenAssetAsync("scope", saved.Id, ct);
            using var copy = new MemoryStream();
            await opened.CopyToAsync(copy, ct);
            Assert.Equal(Png, copy.ToArray());
            Assert.Empty(Directory.GetFiles(root, "*.png"));
            Assert.False(Directory.Exists(options.Value.AssetDirectory));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ImageSaveCoordinator(fresh, brain.Grains).Save("scope", ticket with { Asset = asset with { Name = "different.png" } }, new MemoryStream(Png), ct));
            await Assert.ThrowsAsync<ArgumentException>(() => new ImageSaveCoordinator(fresh, brain.Grains).Save("scope", ticket with { OperationId = Guid.NewGuid().ToString() }, new MemoryStream(Png[..24]), ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LegacyAssetUploadsBeforeServingAndRemainsUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var root = Directory.CreateTempSubdirectory("intochat-legacy-").FullName;
        try
        {
            var checksum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Png));
            var id = LocalFileStore.AssetId("scope", checksum);
            var path = Path.Combine(root, id + ".image");
            await File.WriteAllBytesAsync(path, Png, ct);
            var blobs = new MemoryAssetBlobStore { FailNextUpload = true };
            var files = new LocalFileStore(Options.Create(new LocalFilesOptions { AssetDirectory = root }), new EphemeralDataProtectionProvider(), blobs, brain.Grains);
            await Assert.ThrowsAsync<IOException>(() => files.OpenAssetAsync("scope", id, ct));
            await using var restored = await files.OpenAssetAsync("scope", id, ct);
            Assert.Equal(Png, blobs.Values[id]);
            Assert.Equal(Png, await File.ReadAllBytesAsync(path, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.OpenAssetAsync("other-scope", id, ct));
            var missingHost = new LocalFileStore(Options.Create(new LocalFilesOptions { AssetDirectory = Path.Combine(root, "missing") }), new EphemeralDataProtectionProvider(), blobs, brain.Grains);
            await using var migrated = await missingHost.OpenAssetAsync("scope", id, ct);
            Assert.Equal(Png.Length, migrated.Length);
        }
        finally { Directory.Delete(root, true); }
    }
}
