using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tests.Harness;

[Alias("probe.source")]
[Description("Emits probe facts when poked")]
public partial interface IProbeSource : INeuron, IHandle<Poke>, IEmit<ProbeFact>;

[Alias("probe.sink")]
[Description("Receives whatever is routed at it; declares no handlers")]
public partial interface IProbeSink : INeuron;

[Alias("probe.echo")]
[Description("Re-emits every probe fact routed at it; the raw material of a routing cycle")]
public partial interface IProbeEcho : INeuron, IHandle<Poke>, IEmit<ProbeFact>;
