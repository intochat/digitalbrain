using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace DigitalBrain.Files;

internal sealed class ImageSaveCoordinator(WorkspaceFileStore files, IGrainFactory grains)
{
    public async Task<SavedFile> Save(string scope, SaveTicket ticket, Stream png, CancellationToken ct)
    {
        if (!Guid.TryParse(ticket.OperationId, out _)) { throw new ArgumentException("Use a unique operation ID."); }
        WorkspaceFileStore.ValidateAssetId(scope, ticket.Asset.Id);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\0" + ticket.Asset.DocumentId + "\0" + ticket.OperationId)));
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await png.ReadAsync(buffer, ct)) != 0)
        {
            if (content.Length + count > FilesOptions.MaxExportBytes) { throw new ArgumentException("The exported image exceeds 128 MiB."); }
            await content.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var bytes = content.ToArray();
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) { throw new ArgumentException("Save copy requires a PNG image."); }
        ImageHeader.ValidatePng(bytes);
        var size = ImageHeader.Read(bytes);
        var expectedWidth = ticket.Recipe.Crop?.Width ?? ticket.Asset.Width;
        var expectedHeight = ticket.Recipe.Crop?.Height ?? ticket.Asset.Height;
        if (size.Width != expectedWidth || size.Height != expectedHeight) { throw new ArgumentException("Export dimensions do not match the saved editing revision."); }
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var operation = grains.GetGrain<IImageSaveOperation>(key);
        var previousOperation = await operation.Read().WaitAsync(ct);
        if (ticket.Result is { } saved && saved.Checksum != checksum) { throw new InvalidOperationException("This save operation was already used for different content."); }
        // Old journals are only migration evidence. Never update/delete them or depend on the source path.
        var journal = files.LegacyJournalPath(scope, key);
        if (previousOperation.Result is null && File.Exists(journal))
        {
            using var old = JsonDocument.Parse(await File.ReadAllTextAsync(journal, ct));
            if (!old.RootElement.TryGetProperty("Checksum", out var value) || value.GetString() != checksum)
            { throw new InvalidOperationException("This legacy save operation belongs to different content."); }
        }
        var name = Path.GetFileNameWithoutExtension(ticket.Asset.Name) + "-edited-" + key[..8] + ".png";
        var result = new SavedFile("asset:" + key, name, checksum, bytes.Length);
        await operation.Prepare(result).WaitAsync(ct);
        var asset = new ImageAsset(WorkspaceFileStore.AssetId(scope, checksum), name, size.Width, size.Height, checksum, result.EntryId, key);
        await files.StoreImageAsync(scope, asset, bytes, ct);
        await operation.Uploaded().WaitAsync(ct);
        return await operation.Commit().WaitAsync(ct);
    }
}
