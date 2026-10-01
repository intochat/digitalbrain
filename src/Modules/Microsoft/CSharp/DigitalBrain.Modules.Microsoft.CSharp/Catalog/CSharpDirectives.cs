using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp;

// Maps a contracts assembly to the #:project line a sandbox script uses to reference it.
// The directive is a sandbox concern, not a property of the neuron: the registry knows what
// exists, this knows how a file-based app links to it.
public sealed class CSharpDirectives(IOptions<CSharpOptions> options)
{
    // The source tree does not move while the host runs, so each walk's answer is kept.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> _found = new(StringComparer.OrdinalIgnoreCase);

    public string? Directive(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        if (options.Value.SourceRoot is not { } sourceRoot) { return null; }
        return _found.GetOrAdd(assemblyName, name =>
        {
            var project = Directory.EnumerateFiles(Path.Combine(sourceRoot, "src"), name + ".csproj", SearchOption.AllDirectories).FirstOrDefault();
            return project is null ? null
                : "#:project " + CSharpSandbox.SourceMount + "/" + Path.GetRelativePath(sourceRoot, project).Replace('\\', '/');
        });
    }
}
