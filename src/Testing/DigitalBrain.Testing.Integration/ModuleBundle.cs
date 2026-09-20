using System.Reflection;
using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Testing.Integration;

/// <summary>Uses the test build's dependency closure; never builds or resolves packages at runtime.</summary>
public sealed class ModuleBundle
{
    private ModuleBundle(string directory, string entry) { Directory = directory; Entry = entry; }
    public string Directory { get; }
    public string Entry { get; }

    public static ModuleBundle Validate(string directory, IReadOnlyList<ModuleDefinition> modules)
    {
        var manifestPath = Path.Combine(directory, "module-bundle.json");
        if (!File.Exists(manifestPath)) { throw new InvalidOperationException("Missing module-bundle.json. Import Integration.Tests.props and build the test project first."); }
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (manifest.RootElement.GetProperty("version").GetInt32() != 1) { throw new InvalidOperationException("Unsupported module bundle version."); }
        var entry = manifest.RootElement.GetProperty("entry").GetString()!;
        if (string.IsNullOrWhiteSpace(entry) || Path.GetFileName(entry) != entry || entry.Contains('/') || entry.Contains('\\'))
        { throw new InvalidOperationException("Invalid module bundle entry."); }
        foreach (var file in new[] { entry + ".deps.json", entry + ".runtimeconfig.json", "DigitalBrain.Testing.ModuleRunner.dll" })
        {
            if (!File.Exists(Path.Combine(directory, file))) { throw new InvalidOperationException($"Missing module bundle asset '{file}'."); }
        }
        foreach (var module in modules)
        {
            var expected = module.ModuleType.Assembly.GetName();
            var asset = Path.Combine(directory, expected.Name + ".dll");
            if (!File.Exists(asset)) { throw new InvalidOperationException($"Missing module assembly '{expected.Name}'."); }
            var actual = AssemblyName.GetAssemblyName(asset);
            if (actual.FullName != expected.FullName) { throw new InvalidOperationException($"Module assembly identity conflict for '{expected.Name}'."); }
        }
        return new(Path.GetFullPath(directory), entry);
    }
}
