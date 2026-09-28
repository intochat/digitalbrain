using DigitalBrain.Testing.Unit;
namespace DigitalBrain.Files.Tests;

public sealed class ImageSaveFacts
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==");

    [Fact]
    public async Task SaveSurvivesFreshServiceAndRetriesWithoutDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var blobs = new MemoryAssetBlobStore();
        var files = new WorkspaceFileStore(blobs, brain.Grains);
        var asset = await files.UploadImageAsync("scope", "Untitled.png", new MemoryStream(Png), ct);
        var ticket = new SaveTicket(Guid.NewGuid().ToString(), 0, new(), asset);
        blobs.FailNextUpload = true;
        await Assert.ThrowsAsync<IOException>(() => new ImageSaveCoordinator(files, brain.Grains).Save("scope", ticket, new MemoryStream(Png), ct));
        var first = await new ImageSaveCoordinator(files, brain.Grains).Save("scope", ticket, new MemoryStream(Png), ct);
        await brain.DeactivateAsync(brain.Get<IWorkspaceAssets>("scope"), ct);
        await brain.DeactivateAsync(brain.Get<IImageSaveOperation>(first.EntryId[6..]), ct);
        var fresh = new WorkspaceFileStore(blobs, brain.Grains);
        var second = await new ImageSaveCoordinator(fresh, brain.Grains).Save("scope", ticket, new MemoryStream(Png), ct);
        Assert.Equal(first, second);
        var durable = await fresh.ListAsync("scope", 0, "name", "", ct);
        Assert.Equal(2, durable.Items.Count);
        var saved = await fresh.ReadImageAsync("scope", first.EntryId, ct);
        await using var opened = await fresh.OpenAssetAsync("scope", saved.Id, ct);
        using var copy = new MemoryStream();
        await opened.CopyToAsync(copy, ct);
        Assert.Equal(Png, copy.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ImageSaveCoordinator(fresh, brain.Grains).Save("scope", ticket with { Asset = asset with { Name = "different.png" } }, new MemoryStream(Png), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => new ImageSaveCoordinator(fresh, brain.Grains).Save("scope", ticket with { OperationId = Guid.NewGuid().ToString() }, new MemoryStream(Png[..24]), ct));
    }

    [Fact]
    public async Task UploadsAreScopedToTheirWorkspaceAndRejectNonImages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var files = new WorkspaceFileStore(new MemoryAssetBlobStore(), brain.Grains);
        var asset = await files.UploadImageAsync("workspace-a", "Untitled.png", new MemoryStream(Png), ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.OpenAssetAsync("workspace-b", asset.Id, ct));
        Assert.Empty((await files.ListAsync("workspace-b", 0, "name", "", ct)).Items);
        await Assert.ThrowsAsync<ArgumentException>(() => files.UploadImageAsync("workspace-a", "readme.txt", new MemoryStream(Png), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => files.UploadImageAsync("workspace-a", "huge.png", new MemoryStream(new byte[WorkspaceFileStore.MaxSourceBytes + 1]), ct));
    }
}
