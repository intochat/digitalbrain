using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;

// T2 polyrepo: FileSystem connector impl moved from src/DigitalBrain.Hosting/Microsoft/Windows/FileSystem.cs to Connectors (per vision §4 "extract Google/fs/http from current experiences" + plan "GrainType 'filesystem'").
// 1.0/2.0 comments preserved/adapted; namespace now Connectors.Experiences; class/interface self-exp names (FileSystemConnectorGrain / IFileSystemConnectorNeuron).
// GrainType("filesystem") set explicitly (per plan); distribution/activation/INeuron(key) unchanged. pa-files logic + GetPaFilesBase identical. No behavior change. os/*.ino seeds read-only.
namespace DigitalBrain.Sdk.Experiences;

public interface IFileSystemConnectorNeuron : INeuron, IHandle<SaveFileRequest>, IHandle<ListDirRequest>
{
}

[GrainType("filesystem")]
public sealed class FileSystemConnectorGrain : Neuron, IFileSystemConnectorNeuron
{
    public async Task HandleAsync(SaveFileRequest request, CancellationToken cancellationToken)
    {
        switch (request.Op)
        {
            case FileSave save:
            {
                try
                {
                    var safePath = GetSafePath(save.Path);
                    var dir = Path.GetDirectoryName(safePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    await File.WriteAllTextAsync(safePath, save.Content, cancellationToken);
                    var info = new FileInfo(safePath);

                    await Emit(new FileSaved(safePath, info.Length, save.Description ?? "saved via PA"));
                    await Emit(new NeuronTelemetry(Self, "FileSaved", new Dictionary<string, string>
                    {
                        ["path"] = safePath,
                        ["bytes"] = info.Length.ToString()
                    }));
                }
                catch (Exception ex)
                {
                    await Emit(new FileSaved(save.Path, 0, "error: " + ex.Message));
                }
                break;
            }
            case FileRead read:
            {
                try
                {
                    var safePath = GetSafePath(read.Path);
                    string content = "";
                    var msg = read.Description ?? "read via PA";
                    if (File.Exists(safePath))
                    {
                        content = await File.ReadAllTextAsync(safePath, cancellationToken);
                    }
                    else
                    {
                        msg = "not found";
                    }

                    await Emit(new FileReadResult(safePath, content, msg));
                    await Emit(new NeuronTelemetry(Self, "FileRead", new Dictionary<string, string>
                    {
                        ["path"] = safePath,
                        ["bytes"] = content.Length.ToString()
                    }));
                }
                catch (Exception ex)
                {
                    await Emit(new FileReadResult(read.Path, "", "error: " + ex.Message));
                }
                break;
            }
        }
    }

    public async Task HandleAsync(ListDirRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var safeDir = GetSafePath(request.Path);
            var entries = new List<string>();
            string msg = request.Description ?? "listed via PA";

            if (Directory.Exists(safeDir))
            {
                foreach (var e in Directory.EnumerateFileSystemEntries(safeDir))
                {
                    var name = Path.GetFileName(e) ?? e;
                    var isDir = Directory.Exists(e);
                    entries.Add(isDir ? name + "/" : name);
                }
            }
            else
            {
                msg = "not found";
            }

            await Emit(new DirListResult(safeDir, entries, msg));
            await Emit(new NeuronTelemetry(Self, "DirListed", new Dictionary<string, string>
            {
                ["path"] = safeDir,
                ["count"] = entries.Count.ToString()
            }));
        }
        catch (Exception ex)
        {
            await Emit(new DirListResult(request.Path, Array.Empty<string>(), "error: " + ex.Message));
        }
    }

    private static string GetSafePath(string requested)
    {
        var baseDir = GetPaFilesBase();
        if (Path.IsPathRooted(requested))
        {
            // deny absolute/rooted paths (was security hole: rule or un-granted SaveFileRequest could target any fs location)
            // fallback keeps only the leaf name under pa-files base (relative-only policy)
            requested = Path.GetFileName(requested) ?? "out.bin";
        }
        var combined = Path.Combine(baseDir, requested);
        return Path.GetFullPath(combined);
    }

    // Stable base for the user's personal pa-files/ (first-class durable FS for the PA).
    // Walks up from current dir (handles both "dotnet run ./final/start.cs" from workspace root
    // and the script host sometimes setting content root under final/) until it finds the
    // slnx or the "final" folder. This removes all the previous cwd/content-root variance.
    // Exposed so the daily REPL driver can use the exact same base for its local side-effect
    // guarantee (keeps neuron as the conceptual owner while making the demo always produce
    // visible durable files on disk).
    public static string GetPaFilesBase()
    {
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            // Prefer the true workspace root: the dir that contains the "final" subdir (where we run `dotnet run ./final/start.cs` from).
            if (Directory.Exists(Path.Combine(dir, "final")))
                return Path.Combine(dir, "pa-files");

            // If we are already inside the final/ tree (script host often sets content root here), use its parent as root.
            if (dir.EndsWith("final", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(dir, "DigitalBrain.slnx")))
            {
                var parent = Path.GetDirectoryName(dir);
                if (parent is not null && Directory.Exists(Path.Combine(parent, "final")))
                    return Path.Combine(parent, "pa-files");
                return Path.Combine(dir, "pa-files");
            }

            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "pa-files");
    }
}
