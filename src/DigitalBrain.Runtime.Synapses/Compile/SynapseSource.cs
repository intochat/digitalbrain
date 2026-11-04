namespace DigitalBrain.Runtime.Synapses;

/// <summary>The source of one synapse package: its key, its C# files by path, and an optional icon.</summary>
/// <param name="SourceKey">The package key, e.g. <c>Me.Budget</c>; it names the compiled assembly.</param>
/// <param name="Files">The C# files, keyed by path.</param>
/// <param name="Icon">The package icon bytes, if any.</param>
public sealed record SynapseSource(string SourceKey, IReadOnlyDictionary<string, string> Files, byte[]? Icon);
