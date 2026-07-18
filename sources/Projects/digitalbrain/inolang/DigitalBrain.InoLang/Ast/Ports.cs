using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Ast;

// `Stream` reserves the sigil for IStreamNeuronTarget bindings. The InoLang
// grammar surface for it is deferred to E-INO; today no parser path produces
// this value, but the runtime ABI accepts it so ProductionNeuronHost can
// dispatch on it as soon as a grammar lands. Keeping the value at the end
// preserves the ordinal positions of the pre-existing members.
//
// `Predicate` (E-SDK #58) is the dispatch discriminator for SLM-backed
// `where ... is "..."` neurons (IPredicateNeuronTarget). Like Stream it is
// dormant on the parser side — InoLang predicates are addressed by builtin
// name (`topic-of`), not by a `using` sigil, so no character is reserved.
// ProductionNeuronHost still validates it via EnsureSigil so a misbound
// predicate entry cannot route through a Call grain.
//
// v5 C2: `Synapse` sigil is the broadcast-wire emit sigil (was `Signal`).
// The dead `Inbound` value is gone — no parser path ever produced it.
public enum PortSigil { Synapse, Call, Resource, Stream, Predicate } // ! $ ~
public enum PortKind { Synapse, Neuron }

public sealed record UsingDecl(
    PortSigil Sigil,
    string Name,
    PortKind Kind,
    string TargetFqn,
    string? Key,
    SourceSpan Span);
