using System.Text.Json;
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Aspire.Hosting;

public static class InstalledSet
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new DomainIdJsonConverter() },
    };

    public static HashSet<DomainId> Load(string? path = null)
    {
        path ??= InoPaths.InstalledJson;
        if (!File.Exists(path))
            return new HashSet<DomainId>();

        var json = File.ReadAllText(path);
        var state = JsonSerializer.Deserialize<InstalledState>(json, Options);
        return state is null
            ? new HashSet<DomainId>()
            : new HashSet<DomainId>(state.Installed);
    }

    public static void Save(HashSet<DomainId> installed, string? path = null)
    {
        path ??= InoPaths.InstalledJson;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var state = new InstalledState(installed.ToArray());
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, Options));
        File.Move(tempPath, path, overwrite: true);
    }
}
