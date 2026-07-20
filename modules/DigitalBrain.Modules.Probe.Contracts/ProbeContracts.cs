using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Probe.Contracts;

[GenerateSerializer]
[Alias("db.probe.pinged")]
public sealed record ProbePinged([property: Id(0)] string Note) : Synapse;

[Alias("db.probe.echo")]
public interface IProbeEcho : INeuron
{
    [Alias("Echo")]
    Task<string> EchoAsync(string text);
}
