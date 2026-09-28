namespace DigitalBrain.Microsoft.DotNet;

[GenerateSerializer, Alias("microsoft.dotnet.test-failure")]
public sealed record TestFailure([property: Id(0)] string Name, [property: Id(1)] string Message);