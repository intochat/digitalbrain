using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntoChat.LocalFiles;
namespace IntoChat.Apps;

internal sealed class ImageSaveCoordinator(LocalFileStore files)
{
    public async Task<SavedFile> Save(string scope, SaveTicket ticket, Stream png, CancellationToken ct)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\0" + ticket.Asset.DocumentId + "\0" + ticket.OperationId)));
        var journal = Path.Combine(files.JournalDirectory(scope), key + ".json");

        await using var guard = new FileStream(journal + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await png.ReadAsync(buffer, ct)) != 0)
        {
            if (content.Length + count > LocalFilesOptions.MaxExportBytes) { throw new ArgumentException("The exported image exceeds 128 MiB."); }
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
        var folder = files.DestinationFolder(scope, ticket.Asset.SourceEntryId);
        using var lease = LocalPathLease.Acquire(folder);
        var temporary = Path.Combine(folder, ".intochat-" + Guid.NewGuid().ToString("N") + ".upload");
        SaveJournal? previous = null;
        if (File.Exists(journal))
        {
            previous = JsonSerializer.Deserialize<SaveJournal>(await File.ReadAllTextAsync(journal, ct));
            if (previous is null || previous.Checksum != checksum) { throw new InvalidOperationException("This save operation was already used for different content."); }
            if (File.Exists(Path.Combine(folder, previous.Name)))
            {
                var saved = await File.ReadAllBytesAsync(Path.Combine(folder, previous.Name), ct);
                if (Convert.ToHexStringLower(SHA256.HashData(saved)) != checksum) { throw new IOException("The saved copy was changed outside IntoChat. Save again with a new operation."); }
                var receipt = previous.Result ?? Result(previous.Name);
                await WriteJournal(previous with { Complete = true, Result = receipt });
                return receipt;
            }
            if (previous.Complete) { throw new IOException("The saved copy was removed outside IntoChat. Save again with a new operation."); }
        }
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                LocalPathLease.Verify(output.SafeFileHandle, temporary);
                await output.WriteAsync(bytes, ct);
                await output.FlushAsync(ct);
            }
            var stem = Path.GetFileNameWithoutExtension(ticket.Asset.Name) + "-edited";
            for (var suffix = 0; suffix < 10000; suffix++)
            {
                var name = previous?.Name ?? stem + (suffix == 0 ? "" : " (" + suffix + ")") + ".png";
                previous = null;
                var destination = Path.Combine(folder, name);
                if (File.Exists(destination)) { continue; }
                var result = Result(name);
                await WriteJournal(new(name, checksum, false, result));
                // Move without overwrite publishes only complete bytes; collisions retry with another name.
                try { File.Move(temporary, destination, false); }
                catch (IOException) when (File.Exists(destination)) { continue; }
                await WriteJournal(new(name, checksum, true, result));
                return result;
            }
            throw new IOException("No free copy filename is available.");
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }

        SavedFile Result(string name) => new(files.EntryForSavedFile(scope, ticket.Asset.SourceEntryId, name), name, checksum, bytes.Length);
        async Task WriteJournal(SaveJournal state)
        {
            await File.WriteAllTextAsync(journal + ".next", JsonSerializer.Serialize(state), ct);
            File.Move(journal + ".next", journal, true);
        }
    }
    private sealed record SaveJournal(string Name, string Checksum, bool Complete, SavedFile? Result = null);
}
