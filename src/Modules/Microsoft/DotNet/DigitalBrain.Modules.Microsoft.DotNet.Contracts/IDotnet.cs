using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.DotNet;

[Alias("microsoft.dotnet")]
public interface IDotnet : INeuron
{
    Task<BuildOutcome> Build(BuildRequest request, CancellationToken cancellationToken = default);

    Task<TestOutcome> Test(TestRequest request, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("microsoft.dotnet.build")]
public sealed record BuildRequest([property: Id(0)] string Path, [property: Id(1)] string? ArtifactsPath);

[GenerateSerializer, Alias("microsoft.dotnet.test")]
public sealed record TestRequest(
    [property: Id(0)] string Path,
    [property: Id(1)] string? FilterClass,
    [property: Id(2)] string? ArtifactsPath);

[GenerateSerializer, Alias("microsoft.dotnet.diagnostic")]
public sealed record BuildDiagnostic(
    [property: Id(0)] string Id,
    [property: Id(1)] string Severity,
    [property: Id(2)] string Message,
    [property: Id(3)] string Path,
    [property: Id(4)] int Line);
