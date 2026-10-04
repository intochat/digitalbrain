using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Flutter.Collection;

namespace DigitalBrain.Files;

internal sealed class WorkspaceFileStore(IAssetBlobStore blobs, IGrainFactory grains)
{
    internal const string WorkspaceRoot = "workspace-assets";
    internal const long MaxSourceBytes = 32 * 1024 * 1024;
    internal const long MaxExportBytes = 128 * 1024 * 1024;
    private const string AssetEntry = "asset:";

    public async Task<DirectoryPage> ListAsync(string scope, int offset, string sort, string filter, CancellationToken ct)
    {
        if (offset < 0 || offset > 100000 || filter.Length > 200) { throw new ArgumentException("Invalid file listing request."); }
        var assets = (await grains.GetGrain<IWorkspaceAssets>(scope).List().WaitAsync(ct))
            .Where(asset => asset.Image.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        var ordered = sort switch
        {
            "date" => assets.OrderByDescending(asset => asset.CreatedAt).ThenBy(asset => asset.Image.DocumentId, StringComparer.Ordinal),
            "size" => assets.OrderByDescending(asset => asset.Bytes).ThenBy(asset => asset.Image.DocumentId, StringComparer.Ordinal),
            _ => assets.OrderBy(asset => asset.Image.Name, StringComparer.OrdinalIgnoreCase).ThenBy(asset => asset.Image.DocumentId, StringComparer.Ordinal),
        };
        var page = ordered.Skip(offset).Take(101).Select(asset => new CollectionItem(AssetEntry + asset.Image.DocumentId, asset.Image.Name,
            "image", null, asset.Bytes, asset.CreatedAt, true)).ToArray();
        return new(WorkspaceRoot, "Workspace Files", null, page.Take(100).ToArray(), page.Length > 100 ? offset + 100 : null, [new(WorkspaceRoot, "Workspace Files")]);
    }

    public async Task<ImageAsset> ReadImageAsync(string scope, string entryId, CancellationToken ct)
    {
        if (!entryId.StartsWith(AssetEntry, StringComparison.Ordinal)) { throw new KeyNotFoundException("The workspace asset was not found."); }
        return await grains.GetGrain<IWorkspaceAssets>(scope).Read(entryId[AssetEntry.Length..]).WaitAsync(ct);
    }

    public async Task<ImageAsset> UploadImageAsync(string scope, string name, Stream content, CancellationToken ct)
    {
        var fileName = Path.GetFileName(name);
        if (fileName.Length is 0 or > 200 || Path.GetExtension(fileName).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg"))
        { throw new ArgumentException("Only PNG and JPEG images can be uploaded."); }
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > MaxSourceBytes) { throw new ArgumentException("Choose an image smaller than 32 MiB."); }
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var bytes = buffer.ToArray();
        var (width, height) = ImageHeader.Read(bytes);
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var documentId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\0" + fileName + "\0" + checksum)));
        return await StoreImageAsync(scope, new(AssetId(scope, checksum), fileName, width, height, checksum, AssetEntry + documentId, documentId), bytes, ct);
    }

    public async Task<Stream> OpenAssetAsync(string scope, string assetId, CancellationToken ct)
    {
        ValidateAssetId(scope, assetId);
        var bytes = await blobs.Read(assetId, ct) ?? throw new FileNotFoundException("The workspace image was not found.");
        VerifyBytes(assetId, bytes);
        return new MemoryStream(bytes, writable: false);
    }

    public async Task<ImageAsset> PreserveAssetAsync(string scope, ImageAsset asset, CancellationToken ct)
    {
        await using var stream = await OpenAssetAsync(scope, asset.Id, ct);
        return await grains.GetGrain<IWorkspaceAssets>(scope).Register(new(asset with { SourceEntryId = AssetEntry + asset.DocumentId }, stream.Length, DateTimeOffset.UtcNow)).WaitAsync(ct);
    }

    public async Task<ImageAsset> StoreImageAsync(string scope, ImageAsset asset, byte[] bytes, CancellationToken ct)
    {
        ValidateAssetId(scope, asset.Id);
        VerifyBytes(asset.Id, bytes);
        await blobs.Put(asset.Id, bytes, ct);
        return await grains.GetGrain<IWorkspaceAssets>(scope).Register(new(asset, bytes.Length, DateTimeOffset.UtcNow)).WaitAsync(ct);
    }

    internal static string AssetId(string scope, string checksum) => ScopeKey(scope) + "-" + checksum;
    internal static void ValidateAssetId(string scope, string id)
    {
        if (id.Length != 129 || !id.StartsWith(ScopeKey(scope) + "-", StringComparison.Ordinal) || id.Where((_, i) => i != 64).Any(c => !char.IsAsciiHexDigit(c)))
        {
            throw new UnauthorizedAccessException(
                $"The image does not belong to this workspace (caller scope key {ScopeKey(scope)[..8]}…, asset {id[..Math.Min(8, id.Length)]}…).");
        }
    }
    private static void VerifyBytes(string id, byte[] bytes)
    {
        if (bytes.Length > MaxExportBytes || Convert.ToHexStringLower(SHA256.HashData(bytes)) != id[65..])
        { throw new IOException("The image asset failed its integrity check."); }
    }
    private static string ScopeKey(string scope) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
}
