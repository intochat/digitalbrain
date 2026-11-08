namespace DigitalBrain;

/// <summary>The source of one synapse package: its key, its C# files by path, and an optional icon.</summary>
/// <param name="SourceKey">The package key, e.g. <c>Me.Budget</c>; it names the compiled assembly.</param>
/// <param name="Files">The C# files, keyed by path.</param>
/// <param name="Icon">The package icon bytes, if any.</param>
/// <param name="Bindings">The slot bindings per state alias, <c>Bindings[alias][slotName] = connectionKey</c>; null for none. They travel with the source: deploy, restore and re-deploy read the same ones.</param>
public sealed record SynapseSource(
    string SourceKey,
    IReadOnlyDictionary<string, string> Files,
    byte[]? Icon,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Bindings = null);
