using Orleans;

namespace DigitalBrain.Poc.Abstractions;

[GenerateSerializer]
[Alias("db.poc.probe.ingress.v1")]
public sealed record ProbeIngress([property: Id(0)] string Value) : Synapse;
