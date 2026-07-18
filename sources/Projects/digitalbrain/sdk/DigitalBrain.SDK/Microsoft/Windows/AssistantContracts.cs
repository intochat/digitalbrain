using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Microsoft.Windows;

[GenerateSerializer]
public sealed record Request([property: Id(1)] string Intent) : Synapse;

[GenerateSerializer]
public sealed record Responded([property: Id(1)] string Message) : Synapse;
