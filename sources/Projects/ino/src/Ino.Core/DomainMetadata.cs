namespace Ino.Core;

/// <summary>
/// Source-generated domain descriptor. Emitted at compile time as a static field
/// on the domain's marker class by the Phase 3 source generator. Read by the
/// Phase 2 AppHost composition extension (<see cref="Ino.Aspire.Hosting.WithDomainExtensions.WithDomain{T}"/>)
/// to wire the domain into the domains silo.
/// </summary>
public sealed record DomainMetadata(
    string NeuronId,
    string Version,
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<CanonicalNeuronInfo> CanonicalNeurons,
    IReadOnlyList<ReactiveNeuronInfo> ReactiveNeurons,
    IReadOnlyList<string> UserEntrySchemas,
    IReadOnlyList<Capability> RequiredCapabilities,
    string CoreVersion);
