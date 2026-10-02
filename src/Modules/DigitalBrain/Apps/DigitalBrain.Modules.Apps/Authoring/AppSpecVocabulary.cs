using DigitalBrain.Microsoft.CSharp;

namespace DigitalBrain.Apps;

internal static class AppSpecVocabulary
{
    public static SpecToken[] Read(PackageManifest? manifest, IContractVocabulary? registry) =>
    [
        .. (registry?.Read() ?? []).Select(t => new SpecToken(t.Name, t.Kind, t.QualifiedName, t.Module, t.Description)),
        .. (manifest?.Operations ?? []).Select(t => new SpecToken(t.Name, "operation", t.Name, "App", t.Description)),
        .. (manifest?.Settings ?? []).Select(t => new SpecToken(t.Name, "setting", t.Name, "App", t.Description)),
        .. (manifest?.Accounts ?? []).Select(t => new SpecToken(t.Name, "account", t.Name, "App", t.Description)),
    ];
}
