using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Creator;

public sealed class DynamicMirrorPath : IDynamicMirrorPath
{
    private readonly string root;

    public DynamicMirrorPath(IHostEnvironment env)
    {
        var repoRoot = FindRepoRoot(env.ContentRootPath) ?? env.ContentRootPath;
        root = Path.Combine(repoRoot, "kernel", "DigitalBrain.Domains.Dynamic", "Generated");
    }

    public string For(NeuronId id)
    {
        var slug = id.Value.Split('/').Last();
        return Path.Combine(root, slug);
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DigitalBrain.sln")) ||
                File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")) ||
                File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
