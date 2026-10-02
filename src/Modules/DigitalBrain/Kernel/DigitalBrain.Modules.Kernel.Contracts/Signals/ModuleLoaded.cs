namespace DigitalBrain.Contracts.Signals;

[GenerateSerializer, Alias("brain.module-loaded")]
public sealed record ModuleLoaded([property: Id(0)] string ModuleType) : Signal;
