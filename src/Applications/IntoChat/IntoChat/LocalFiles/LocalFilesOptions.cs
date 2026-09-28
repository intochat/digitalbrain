namespace IntoChat.LocalFiles;

internal sealed class LocalFilesOptions
{
    public Dictionary<string, string> Roots { get; set; } = [];
    // Read-only legacy migration source. New assets and save receipts never write here.
    public string AssetDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntoChat", "assets");
    public const long MaxSourceBytes = 32 * 1024 * 1024;
    public const long MaxExportBytes = 128 * 1024 * 1024;
}
