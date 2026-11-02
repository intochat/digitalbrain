namespace DigitalBrain.Capabilities.Storage.Confinement;

/// <summary>Directories and path segments that stay unreachable even when an allowed root contains them.</summary>
public static class HardDenyList
{
    public static readonly string[] Segments = [".ssh", ".aws", ".gnupg", ".intochat"];

    public static IReadOnlyList<string> For(PathFlavor flavor, string? osStateDir, string? secretsDir)
    {
        var list = new List<string>();
        if (flavor == PathFlavor.Windows)
        {
            list.Add(@"C:\Windows");
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windows)) { list.Add(windows); }
        }
        else
        {
            list.Add("/etc");
            list.Add("/proc");
            list.Add("/sys");
            list.Add("/dev");
            list.Add("/boot");
        }

        if (!string.IsNullOrWhiteSpace(osStateDir)) { list.Add(osStateDir); }
        if (!string.IsNullOrWhiteSpace(secretsDir)) { list.Add(secretsDir); }
        return list;
    }
}
