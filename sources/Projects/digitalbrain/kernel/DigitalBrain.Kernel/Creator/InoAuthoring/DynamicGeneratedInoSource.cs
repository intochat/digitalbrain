using System.Text.Json;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. Production `IInterpretedNeuronSource` impl
// that hands the `InterpretedNeuronRegistry` (#63) the descriptors for
// every Creator-authored `.ino` persisted under the Generated root.
//
// New promotions persist routing metadata in manifest.json, so discovery
// registers their descriptors without opening or compiling the `.ino` body.
// InoDefinitionCache reads and validates that body only on first activation.
// Legacy identity-only manifests retain the prior compile-at-discovery path.
//
// Legacy compile failures are skipped at discovery. Metadata-backed sources
// are hash-checked and linked at first activation; a refusal affects that
// neuron only and does not make a single broken definition fail silo startup.
public sealed class DynamicGeneratedInoSource(
    IInoGeneratedRoot generatedRoot,
    IContractCatalog catalog,
    ILogger<DynamicGeneratedInoSource> logger,
    InoDefinitionCache? definitions = null) : IInterpretedNeuronSource
{
    readonly InoDefinitionCache _definitions = definitions ?? new InoDefinitionCache();

    public Task<IReadOnlyList<InterpretedNeuronRegistration>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var root = generatedRoot.AbsolutePath;
        var registrations = new List<InterpretedNeuronRegistration>();

        if (!Directory.Exists(root))
        {
            logger.LogInformation(
                "DynamicGeneratedInoSource: Generated root '{Root}' does not exist; no Creator-authored neurons to register.",
                root);
            return Task.FromResult<IReadOnlyList<InterpretedNeuronRegistration>>(registrations);
        }

        foreach (var neuronDir in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var inoPath in Directory.EnumerateFiles(neuronDir, "*.ino"))
            {
                if (TryBuildRegistration(inoPath, out var registration))
                    registrations.Add(registration!);
            }
        }

        logger.LogInformation(
            "DynamicGeneratedInoSource: discovered {Count} Creator-authored interpreted neuron(s) under '{Root}'.",
            registrations.Count, root);

        return Task.FromResult<IReadOnlyList<InterpretedNeuronRegistration>>(registrations);
    }

    bool TryBuildRegistration(string inoPath, out InterpretedNeuronRegistration? registration)
    {
        registration = null;

        // InoNeuronStore writes .ino BEFORE manifest.json (the
        // "atomic-ish" path). If the silo crashed between those two
        // writes, the .ino is an orphan — skip it so a half-written
        // promotion never re-registers. The manifest itself doesn't
        // gate dispatch (the registry holds the canonical descriptor)
        // but its absence is the documented "ino-only persist failed"
        // signal that must NOT be re-registered at next start.
        var manifestPath = Path.Combine(
            Path.GetDirectoryName(inoPath)!, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            logger.LogWarning(
                "DynamicGeneratedInoSource: '{InoPath}' has no sibling manifest.json; skipping (likely an orphan from a crashed promotion).",
                inoPath);
            return false;
        }

        InoNeuronManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<InoNeuronManifest>(
                File.ReadAllText(manifestPath));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex,
                "DynamicGeneratedInoSource: failed to read manifest for '{InoPath}'; using legacy source discovery.",
                inoPath);
            manifest = null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex,
                "DynamicGeneratedInoSource: failed to read manifest for '{InoPath}'; using legacy source discovery.",
                inoPath);
            manifest = null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "DynamicGeneratedInoSource: failed to read manifest for '{InoPath}'; using legacy source discovery.",
                inoPath);
            manifest = null;
        }

        if (TryBuildManifestRegistration(manifest, inoPath, out registration))
            return true;

        return TryBuildLegacyRegistration(inoPath, out registration);
    }

    bool TryBuildManifestRegistration(
        InoNeuronManifest? manifest,
        string inoPath,
        out InterpretedNeuronRegistration? registration)
    {
        registration = null;
        if (manifest is not
            {
                Fqn: { Length: > 0 },
                SourceFileName: { Length: > 0 },
                Incoming: not null,
                Outgoing: not null,
                HandledSignalSubscriptions: not null,
                SourceSha256: { Length: > 0 },
            })
        {
            return false;
        }

        if (!string.Equals(
                manifest.SourceFileName,
                Path.GetFileName(inoPath),
                StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "DynamicGeneratedInoSource: manifest source filename '{ManifestFile}' does not match '{InoPath}'; skipping lazy metadata.",
                manifest.SourceFileName,
                inoPath);
            return false;
        }

        var sourceKey = "generated:" + manifest.Fqn + ":" + manifest.SourceSha256;
        _definitions.RegisterFile(sourceKey, inoPath);
        var descriptor = new NeuronDescriptor(
            manifest.Fqn,
            manifest.Incoming,
            manifest.Outgoing,
            InoLangSource: string.Empty,
            InoLangSourceCacheKey: sourceKey,
            InoLangSourceSha256: manifest.SourceSha256);
        registration = new InterpretedNeuronRegistration(
            descriptor,
            manifest.HandledSignalSubscriptions);
        return true;
    }

    bool TryBuildLegacyRegistration(
        string inoPath,
        out InterpretedNeuronRegistration? registration)
    {
        registration = null;
        string source;
        try
        {
            source = File.ReadAllText(inoPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex,
                "DynamicGeneratedInoSource: failed to read '{InoPath}'; skipping.", inoPath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex,
                "DynamicGeneratedInoSource: failed to read '{InoPath}'; skipping.", inoPath);
            return false;
        }

        var compiled = InoCompiler.Compile(source, catalog);
        if (!compiled.Success || compiled.Linked is null)
        {
            logger.LogWarning(
                "DynamicGeneratedInoSource: legacy '{InoPath}' failed to compile; skipping. Diagnostics: {Diagnostics}",
                inoPath,
                string.Join(" | ", compiled.Diagnostics.Select(d => d.Code + " " + d.Message)));
            return false;
        }

        registration = LinkedPortCatalogContributor.BuildRegistration(source, compiled.Linked);
        return true;
    }
}
