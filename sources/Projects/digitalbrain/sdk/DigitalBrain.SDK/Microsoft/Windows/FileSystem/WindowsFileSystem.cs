using System.Text;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.Microsoft.Windows.FileSystem;

// v5 SDK extension: a callable connector neuron for local filesystem access.
// Other neurons reach this via `using fs = neuron(DigitalBrain.SDK.Windows.FileSystem)`
// and `ask fs to "read C:/path/to/file.txt"` style prompts. Verbs supported:
//   read <path>     -> UTF-8 text contents (size-capped at MaxReadBytes)
//   list <dir>      -> newline-joined entries (capped at MaxListEntries)
//   exists <path>   -> "true" / "false"
//   info <path>     -> "file <size> <utcModified>" | "dir <utcModified>" | "missing"
//
// Read intentionally caps the response so an enthusiastic caller can't OOM the
// silo with a multi-GB file. Binary content surfaces as `Error: <path> is not
// valid UTF-8 text.` so the caller can decide how to handle it explicitly.
[GrainType(NeuronTargetFqn)]
public sealed class WindowsFileSystem(ILogger<WindowsFileSystem> logger)
    : Neuron, ICallNeuronTarget
{
    public const string NeuronTargetFqn = "DigitalBrain.SDK.Windows.FileSystem";

    const int MaxReadBytes    = 10 * 1024 * 1024;
    const int MaxListEntries  = 200;

    public Task<string> AskAsync(string prompt)
    {
        var trimmed = (prompt ?? "").Trim();
        logger.LogInformation("FileSystem neuron invoked: {Prompt}", trimmed);

        var (verb, arg) = SplitVerb(trimmed);
        return Task.FromResult(verb switch
        {
            "read"   => Read(arg),
            "list"   => List(arg),
            "exists" => File.Exists(arg) || Directory.Exists(arg) ? "true" : "false",
            "info"   => Info(arg),
            "write"  => Write(arg),
            _        => $"Error: unknown verb '{verb}'. Expected read | list | exists | info | write."
        });
    }

    static (string Verb, string Arg) SplitVerb(string prompt)
    {
        var space = prompt.IndexOf(' ');
        return space < 0
            ? (prompt.ToLowerInvariant(), "")
            : (prompt[..space].ToLowerInvariant(), prompt[(space + 1)..].Trim());
    }

    static string Write(string arg)
    {
        var (path, content) = SplitWriteArg(arg);
        if (string.IsNullOrWhiteSpace(path)) return "Error: write requires a path.";

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, content, Encoding.UTF8);
            return "success";
        }
        catch (Exception ex)
        {
            return $"Error: failed to write {path}. Detail: {ex.Message}";
        }
    }

    static (string Path, string Content) SplitWriteArg(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return ("", "");
        arg = arg.Trim();
        if (arg.StartsWith('"'))
        {
            var nextQuote = arg.IndexOf('"', 1);
            if (nextQuote > 1)
            {
                var path = arg[1..nextQuote];
                var content = arg[(nextQuote + 1)..].Trim();
                return (path, content);
            }
        }
        var space = arg.IndexOf(' ');
        return space < 0
            ? (arg, "")
            : (arg[..space], arg[(space + 1)..].Trim());
    }


    static string Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: read requires a path.";
        if (!File.Exists(path))               return $"Error: file not found: {path}";

        var info = new FileInfo(path);
        if (info.Length > MaxReadBytes)
            return $"Error: {path} is {info.Length:N0} bytes; max read is {MaxReadBytes:N0}.";

        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (DecoderFallbackException)
        {
            return $"Error: {path} is not valid UTF-8 text.";
        }
        catch (Exception ex)
        {
            return $"Error: failed to read {path}. Detail: {ex.Message}";
        }
    }

    static string List(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return "Error: list requires a directory.";
        if (!Directory.Exists(dir))         return $"Error: directory not found: {dir}";

        try
        {
            var entries = Directory.EnumerateFileSystemEntries(dir)
                .Take(MaxListEntries + 1)
                .Select(Path.GetFileName)
                .ToArray();

            var capped  = entries.Length > MaxListEntries;
            var visible = capped ? entries.Take(MaxListEntries) : entries;
            var body    = string.Join('\n', visible);
            return capped ? $"{body}\n... ({MaxListEntries}+ entries — truncated)" : body;
        }
        catch (Exception ex)
        {
            return $"Error: failed to list {dir}. Detail: {ex.Message}";
        }
    }

    static string Info(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: info requires a path.";

        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return $"file {info.Length} {info.LastWriteTimeUtc:O}";
        }
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return $"dir {info.LastWriteTimeUtc:O}";
        }
        return "missing";
    }
}
