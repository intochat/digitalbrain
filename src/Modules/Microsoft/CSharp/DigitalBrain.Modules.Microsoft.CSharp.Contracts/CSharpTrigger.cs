namespace DigitalBrain.Microsoft.CSharp;

// Neuron is the source's grain id ("time.timer/tea"); Signal is the signal type's name ("TimerTick").
[GenerateSerializer, Alias("microsoft.csharp.trigger")]
public sealed record CSharpTrigger([property: Id(0)] string Neuron, [property: Id(1)] string Signal);
