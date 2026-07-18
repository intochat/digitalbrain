using System.Text.Json;

namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. File-backed persistence for Creator-authored
// `.ino` documents. Layout (mirrors the dynamic-domain triplet convention
// inherited from #50/#52/#54):
//
//     <root>/<neuron-id>/<slug>.ino
//     <root>/<neuron-id>/manifest.json
//
// `<neuron-id>` is the sanitized FQN — colons / forward slashes / dots
// become dashes so the path is filesystem-safe on Windows. `<slug>` is
// the last dot-separated segment of the FQN, lowercased.
//
// Why two files instead of one with frontmatter:
//   * The `.ino` stays the SOURCE OF TRUTH — it's what `InoCompiler` and
//     `InoScenarioProjection.RunAsync` consume. Splitting the manifest
//     keeps the .ino round-trippable through standard InoLang tools
//     without a custom strip-frontmatter pass.
//   * Per the E-SDK #63 carry-over: a future marketplace install bundle
//     would ship the .ino + signature/license separately; mirroring that
//     two-file layout now keeps the on-disk shape forward-compatible.
public sealed class InoNeuronStore(IInoGeneratedRoot generatedRoot) : IInoNeuronStore
{
    static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = true,
    };

    public async Task<string> SaveAsync(
        InoNeuronManifest manifest,
        string inoSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(inoSource);

        var neuronDir = Path.Combine(generatedRoot.AbsolutePath, manifest.NeuronId);
        Directory.CreateDirectory(neuronDir);

        var inoPath = Path.Combine(neuronDir, manifest.SourceFileName);
        var manifestPath = Path.Combine(neuronDir, "manifest.json");

        // Atomic-ish write: source first, manifest second. If the silo
        // crashes between them the discovery source will see a .ino with
        // no manifest and skip it — preferable to a manifest pointing at
        // a half-written .ino which would crash the linker at the next
        // silo start.
        await File.WriteAllTextAsync(inoPath, inoSource, cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, ManifestOptions),
                cancellationToken)
            .ConfigureAwait(false);

        return Path.Combine(manifest.NeuronId, manifest.SourceFileName)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    // Slug = the last dot-separated segment of the FQN, lowercased. Used
    // by both the loop (when constructing the manifest) and the discovery
    // source (matching the `.ino` filename inside a neuron directory).
    public static string SlugFromFqn(string fqn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fqn);
        var lastDot = fqn.LastIndexOf('.');
        var tail = lastDot < 0 ? fqn : fqn[(lastDot + 1)..];
        return tail.ToLowerInvariant();
    }

    // NeuronId = filesystem-safe representation of the FQN. Preserves the
    // dot separator (Windows/Linux/macOS all tolerate dots in directory
    // names) so two distinct FQNs cannot collapse to the same directory.
    // The earlier dot-to-dash mapping collided `Foo.Bar` with `Foo-Bar` —
    // and the InoLang lexer permits hyphens in identifiers, so both shapes
    // are reachable from a Creator-authored unit. Only path-separator
    // characters and the Orleans key-delimiter `:` get rewritten.
    public static string NeuronIdFromFqn(string fqn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fqn);
        return fqn.Replace('/', '-').Replace(':', '-').ToLowerInvariant();
    }
}
