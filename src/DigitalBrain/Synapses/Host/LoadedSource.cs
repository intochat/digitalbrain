using System.Reflection;

namespace DigitalBrain.Units;

/// <summary>A synapse source compiled and loaded into its own assembly, with what the assembly declares.</summary>
/// <param name="SourceKey">The source's key.</param>
/// <param name="Hash">The content hash of the build that was loaded.</param>
/// <param name="Assembly">The loaded assembly.</param>
/// <param name="Catalog">The states and synapses of the assembly.</param>
/// <param name="Signals">The concrete <see cref="Signal"/> types the assembly declares with <c>[Signal]</c>.</param>
/// <param name="States">The <c>[State]</c> record types the assembly declares.</param>
public sealed record LoadedSource(
    string SourceKey,
    string Hash,
    Assembly Assembly,
    SynapseCatalog Catalog,
    IReadOnlyList<Type> Signals,
    IReadOnlyList<Type> States);
