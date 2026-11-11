using DigitalBrain.Units;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>The result of compiling a <see cref="SynapseSource"/>.</summary>
/// <param name="Success">True when no error was reported and the assembly was emitted.</param>
/// <param name="Diagnostics">Every compiler and analyzer diagnostic.</param>
/// <param name="Assembly">The emitted assembly bytes; empty on failure.</param>
/// <param name="Symbols">The portable PDB bytes; empty on failure.</param>
/// <param name="Hash">SHA-256 hex over the reference fingerprint, the source key and the sorted files.</param>
/// <param name="AssemblyName">The assembly name, <c>synapse.{SourceKey}.{first 8 hex of Hash}</c>.</param>
public sealed record SynapseBuild(bool Success, IReadOnlyList<SynapseDiagnostic> Diagnostics, byte[] Assembly, byte[] Symbols, string Hash, string AssemblyName);
