using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Microsoft.Windows;

[GenerateSerializer]
public sealed record ExecuteCommand(
    [property: Id(1)] string AppName,
    [property: Id(2)] string Args
) : Synapse;
