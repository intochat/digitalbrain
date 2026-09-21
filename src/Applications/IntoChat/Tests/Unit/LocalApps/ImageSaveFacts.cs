using IntoChat.Apps;
using IntoChat.LocalFiles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
namespace IntoChat.Tests.Unit.LocalApps;

public sealed class ImageSaveFacts
{
    [Fact]
    public async Task RepeatedSaveReturnsOneCopyAndNeverReplacesSourceOrCollision()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-save-").FullName;
        try
        {
            var source = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==");
            await File.WriteAllBytesAsync(Path.Combine(root, "Untitled.png"), source, ct);
            await File.WriteAllTextAsync(Path.Combine(root, "Untitled-edited.png"), "existing", ct);
            var files = new LocalFileStore(Options.Create(new LocalFilesOptions { Roots = new() { ["downloads"] = root }, AssetDirectory = Path.Combine(root, "assets") }), new EphemeralDataProtectionProvider());
            var listing = await files.ListAsync("scope", null, 0, "name", "Untitled.png", ct);
            var asset = await files.SnapshotImageAsync("scope", Assert.Single(listing.Items).Id, ct);
            var ticket = new SaveTicket(Guid.NewGuid().ToString(), 0, new(), asset);
            var occupiedTemporary = Path.Combine(root, ".intochat-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("scope\0" + asset.DocumentId + "\0" + ticket.OperationId))) + ".upload");
            await File.WriteAllTextAsync(occupiedTemporary, "Do not truncate an existing file", ct);
            var first = await new ImageSaveCoordinator(files).Save("scope", ticket, new MemoryStream(source), ct);
            var second = await new ImageSaveCoordinator(files).Save("scope", ticket, new MemoryStream(source), ct);
            Assert.Equal(first, second);
            Assert.Equal("Do not truncate an existing file", await File.ReadAllTextAsync(occupiedTemporary, ct));
            Assert.Equal("Untitled-edited (1).png", first.Name);
            Assert.Equal(source, await File.ReadAllBytesAsync(Path.Combine(root, "Untitled.png"), ct));
            Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(root, "Untitled-edited.png"), ct));
            Assert.Equal(3, Directory.GetFiles(root, "*.png").Length);
            await Assert.ThrowsAsync<ArgumentException>(() => new ImageSaveCoordinator(files).Save("scope", ticket with { OperationId = Guid.NewGuid().ToString() }, new MemoryStream(source[..24]), ct));
        }
        finally { Directory.Delete(root, true); }
    }
}
