using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tests.Harness;

[GenerateSerializer]
[Alias("probe.poke")]
[Description("Ask a probe source to emit a fact")]
public sealed record Poke([property: Id(0)] string Text) : Synapse;

[GenerateSerializer]
[Alias("probe.fact")]
[Description("A fact emitted by a probe source; no compiled handler exists anywhere")]
public sealed record ProbeFact([property: Id(0)] string Text) : Synapse;
