using Orleans;

namespace DigitalBrain.Poc.Abstractions;

[GenerateSerializer]
[Alias("db.poc.other.ingress.v1")]
public sealed record OtherProbeIngress([property: Id(0)] string Value) : Synapse;
