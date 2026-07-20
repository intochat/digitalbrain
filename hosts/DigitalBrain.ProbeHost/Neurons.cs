using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans;

namespace DigitalBrain.ProbeHost;

[GenerateSerializer]
[Alias("probe.remembered")]
internal sealed record Remembered([property: Id(0)] string What) : Synapse;

internal sealed class Recorder : Neuron, IHandle<Remembered>
{
    public Task HandleAsync(Remembered synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
